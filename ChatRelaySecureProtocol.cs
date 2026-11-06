using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace ChatRelay;

public sealed record ChatRelayChat(string PlayerName, string SteamId, string Team, string Message);

public static class ChatRelaySecureProtocol
{
    public const int KeyBytes = 32;
    public const int MaximumDatagramBytes = 8192;
    public const int MaximumPlaintextBytes = 4096;
    public const int TimestampWindowSeconds = 120;
    public const int HeaderBytes = 26;
    public const int TagBytes = 16;

    private static readonly byte[] Magic = "CRLY"u8.ToArray();
    private static readonly byte[] Context = "ChatRelay/udp/v1"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool TryParseKey(string? base64Key, out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(base64Key)) return false;
        try
        {
            var candidate = Convert.FromBase64String(base64Key);
            if (candidate.Length != KeyBytes || !string.Equals(Convert.ToBase64String(candidate), base64Key, StringComparison.Ordinal))
            {
                CryptographicOperations.ZeroMemory(candidate);
                return false;
            }
            key = candidate;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string GenerateKey()
    {
        var key = RandomNumberGenerator.GetBytes(KeyBytes);
        try { return Convert.ToBase64String(key); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public static byte[] Encrypt(ChatRelayChat chat, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, DateTimeOffset timestamp)
    {
        if (key.Length != KeyBytes) throw new ArgumentException("A 32-byte key is required.", nameof(key));
        if (nonce.Length != 12) throw new ArgumentException("A 12-byte nonce is required.", nameof(nonce));
        ValidateChat(chat);

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(chat, JsonOptions);
        try
        {
            if (plaintext.Length > MaximumPlaintextBytes) throw new ArgumentException("Chat payload exceeds the protocol limit.", nameof(chat));
            var packet = new byte[HeaderBytes + plaintext.Length + TagBytes];
            Magic.CopyTo(packet, 0);
            packet[4] = 1;
            packet[5] = 1;
            BinaryPrimitives.WriteInt64BigEndian(packet.AsSpan(6, 8), timestamp.ToUnixTimeMilliseconds());
            nonce.CopyTo(packet.AsSpan(14, 12));
            var associatedData = CreateAssociatedData(packet.AsSpan(0, HeaderBytes));
            try
            {
                using var aes = new AesGcm(key, TagBytes);
                aes.Encrypt(nonce, plaintext, packet.AsSpan(HeaderBytes, plaintext.Length),
                    packet.AsSpan(HeaderBytes + plaintext.Length, TagBytes), associatedData);
                return packet;
            }
            finally { CryptographicOperations.ZeroMemory(associatedData); }
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public static bool TryDecrypt(ReadOnlySpan<byte> packet, ReadOnlySpan<byte> key, DateTimeOffset now, out ChatRelayChat? chat, out string rejection)
    {
        chat = null;
        rejection = "invalid secure packet";
        if (key.Length != KeyBytes || packet.Length < HeaderBytes + TagBytes || packet.Length > MaximumDatagramBytes)
        {
            rejection = "invalid packet length";
            return false;
        }
        if (!packet[..4].SequenceEqual(Magic) || packet[4] != 1 || packet[5] != 1)
        {
            rejection = "unsupported protocol version";
            return false;
        }

        var timestamp = BinaryPrimitives.ReadInt64BigEndian(packet.Slice(6, 8));
        DateTimeOffset sentAt;
        try { sentAt = DateTimeOffset.FromUnixTimeMilliseconds(timestamp); }
        catch (ArgumentOutOfRangeException)
        {
            rejection = "invalid timestamp";
            return false;
        }
        if ((now - sentAt).Duration() > TimeSpan.FromSeconds(TimestampWindowSeconds))
        {
            rejection = "timestamp outside acceptance window";
            return false;
        }

        var ciphertextLength = packet.Length - HeaderBytes - TagBytes;
        if (ciphertextLength <= 0 || ciphertextLength > MaximumPlaintextBytes)
        {
            rejection = "invalid ciphertext length";
            return false;
        }

        var plaintext = new byte[ciphertextLength];
        var associatedData = CreateAssociatedData(packet[..HeaderBytes]);
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(packet.Slice(14, 12), packet.Slice(HeaderBytes, ciphertextLength),
                packet[^TagBytes..], plaintext, associatedData);
            chat = JsonSerializer.Deserialize<ChatRelayChat>(plaintext, JsonOptions);
            if (chat is null)
            {
                rejection = "invalid message schema";
                return false;
            }
            ValidateChat(chat);
            rejection = string.Empty;
            return true;
        }
        catch (CryptographicException)
        {
            rejection = "authentication failed";
            return false;
        }
        catch (JsonException)
        {
            rejection = "invalid message schema";
            return false;
        }
        catch (ArgumentException)
        {
            rejection = "invalid message schema";
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    public static byte[] CreateAssociatedData(ReadOnlySpan<byte> header)
    {
        var associatedData = new byte[Context.Length + header.Length];
        Context.CopyTo(associatedData, 0);
        header.CopyTo(associatedData.AsSpan(Context.Length));
        return associatedData;
    }

    private static void ValidateChat(ChatRelayChat chat)
    {
        if (chat is null || chat.PlayerName is null || chat.SteamId is null || chat.Team is null || chat.Message is null ||
            string.IsNullOrWhiteSpace(chat.Message) || chat.Message.Length > 2048 ||
            chat.PlayerName.Length > 128 || chat.Team.Length > 32 || chat.SteamId.Length > 64)
            throw new ArgumentException("Invalid chat message fields.");
    }

}

public sealed class ChatRelayNonceSequence
{
    private readonly byte[] _sessionId = RandomNumberGenerator.GetBytes(8);
    private long _counter;

    public byte[] Next()
    {
        var next = Interlocked.Increment(ref _counter);
        if (next > uint.MaxValue) throw new InvalidOperationException("ChatRelay nonce sequence exhausted; restart the sender.");
        var nonce = new byte[12];
        _sessionId.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), (uint)next);
        return nonce;
    }
}

public static class ChatRelayUdpSender
{
    private static readonly ChatRelayNonceSequence Nonces = new();

    public static byte[] CreatePacket(ChatRelayTarget target, string playerName, string message, string team, ulong steamId, DateTimeOffset? timestamp = null)
    {
        var chat = new ChatRelayChat(playerName, steamId.ToString(), team, message);
        if (!ChatRelaySecureProtocol.TryParseKey(target.KeyBase64, out var key))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(target.KeyBase64)
                ? "A valid 32-byte secure key is required; legacy plaintext is no longer supported."
                : "Configured secure key is invalid; refusing to send.");
        try
        {
            return ChatRelaySecureProtocol.Encrypt(chat, key, Nonces.Next(), timestamp ?? DateTimeOffset.UtcNow);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public static async Task SendAsync(UdpClient client, IPEndPoint endPoint, ChatRelayTarget target,
        string playerName, string message, string team, ulong steamId, CancellationToken cancellationToken = default)
    {
        var packet = CreatePacket(target, playerName, message, team, steamId);
        try { await client.SendAsync(packet, endPoint, cancellationToken); }
        finally { CryptographicOperations.ZeroMemory(packet); }
    }

}
