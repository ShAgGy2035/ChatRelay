using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ChatRelay;

public sealed class ChatRelayConfig : BasePluginConfig
{
    public const int MaxTargets = 10;

    [JsonPropertyName("IgnoreCommands")]
    public bool IgnoreCommands { get; set; } = true;

    [JsonPropertyName("OnlyShowCommands")]
    public bool OnlyShowCommands { get; set; } = false;

    [JsonPropertyName("Targets")]
    public List<ChatRelayTarget> Targets { get; set; } = [];

    [JsonPropertyName("TargetIp")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetIp { get; set; }

    [JsonPropertyName("TargetPort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TargetPort { get; set; }

    [JsonPropertyName("SharedToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SharedToken { get; set; }

    [JsonPropertyName("KeyBase64")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KeyBase64 { get; set; }

    [JsonPropertyName("AllowLegacyPlaintext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllowLegacyPlaintext { get; set; }
}

public sealed class ChatRelayTarget
{
    public string KeyBase64 { get; set; } = string.Empty;
    public string TargetIp { get; set; } = "127.0.0.1";
    public int TargetPort { get; set; } = 9090;
    [JsonPropertyName("SharedToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SharedToken { get; set; }

    [JsonPropertyName("AllowLegacyPlaintext")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllowLegacyPlaintext { get; set; }
    public bool? IgnoreCommands { get; set; }
    public bool? OnlyShowCommands { get; set; }
}

internal sealed class ChatRelayDestination(ChatRelayTarget target, IPEndPoint endPoint, UdpClient udpClient) : IDisposable
{
    public ChatRelayTarget Target { get; } = target;
    public IPEndPoint EndPoint { get; } = endPoint;
    public UdpClient UdpClient { get; } = udpClient;

    public void Dispose() => UdpClient.Dispose();
}

public sealed class ChatRelay : BasePlugin, IPluginConfig<ChatRelayConfig>
{
    public override string ModuleName => "ChatRelay UDP";
    public override string ModuleDescription => "Forwards CS2 player chat to a UDP listener";
    public override string ModuleAuthor => "ShAgGy";
    public override string ModuleVersion => "1.0.2";

    public ChatRelayConfig Config { get; set; } = new();
    private IReadOnlyList<ChatRelayDestination> configuredTargets = [];

    public void OnConfigParsed(ChatRelayConfig config)
    {
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        configuredTargets = GetValidTargets();
        Logger.LogInformation("ChatRelay authenticated UDP v1 is the only supported transport; loaded {TargetCount} valid target(s).", configuredTargets.Count);
        RegisterEventHandler<EventPlayerChat>(OnPlayerChat);
    }

    private HookResult OnPlayerChat(EventPlayerChat @event, GameEventInfo info)
    {
        var player = Utilities.GetPlayerFromUserid(@event.Userid);
        if (player is null || !player.IsValid || string.IsNullOrWhiteSpace(@event.Text))
        {
            Logger.LogDebug("Chat event ignored: invalid player or empty message.");
            return HookResult.Continue;
        }

        var message = @event.Text.Trim();
        var startsWithCommand = message.StartsWith('!') || message.StartsWith('/');

        var team = "[ALL]";
        if (@event.Teamonly)
        {
            team = (CsTeam)player.TeamNum switch
            {
                CsTeam.Terrorist => "[T]",
                CsTeam.CounterTerrorist => "[CT]",
                CsTeam.Spectator => "[SPEC]",
                _ => "[NONE]"
            };
        }

        foreach (var target in configuredTargets)
        {
            var ignoreCommands = target.Target.IgnoreCommands ?? Config.IgnoreCommands;
            var onlyShowCommands = target.Target.OnlyShowCommands ?? Config.OnlyShowCommands;
            if ((ignoreCommands && startsWithCommand) || (onlyShowCommands && !startsWithCommand))
                continue;

            _ = SendUdpPayloadAsync(target, player.PlayerName, message, team, player.SteamID);
        }

        return HookResult.Continue;
    }

    private IReadOnlyList<ChatRelayDestination> GetValidTargets()
    {
        if (!string.IsNullOrWhiteSpace(Config.SharedToken) || Config.AllowLegacyPlaintext.HasValue)
            Logger.LogWarning("Saved root plaintext ChatRelay settings are obsolete and ignored; only authenticated UDP v1 is supported.");

        IEnumerable<ChatRelayTarget> targets;
        if (Config.Targets.Count > 0)
        {
            if (Config.Targets.Count > ChatRelayConfig.MaxTargets)
                Logger.LogWarning("ChatRelay supports at most {MaxTargets} targets; additional targets will be ignored.", ChatRelayConfig.MaxTargets);

            targets = Config.Targets.Take(ChatRelayConfig.MaxTargets);
        }
        else if (!string.IsNullOrWhiteSpace(Config.TargetIp) && Config.TargetPort.HasValue)
        {
            Logger.LogWarning("ChatRelay is using the single-target settings shape. Migrate to the Targets array when convenient.");
            targets =
            [
                new ChatRelayTarget
                {
                    TargetIp = Config.TargetIp,
                    TargetPort = Config.TargetPort.Value,
                    SharedToken = Config.SharedToken ?? string.Empty,
                    KeyBase64 = Config.KeyBase64 ?? string.Empty,
                    AllowLegacyPlaintext = Config.AllowLegacyPlaintext
                }
            ];
        }
        else
        {
            Logger.LogWarning("ChatRelay has no configured UDP targets.");
            return [];
        }

        var validTargets = new List<ChatRelayDestination>();
        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target.TargetIp) || target.TargetPort is < 1 or > 65535)
            {
                Logger.LogWarning("Ignoring invalid ChatRelay UDP target: {TargetIp}:{TargetPort}.", target.TargetIp, target.TargetPort);
                continue;
            }

            if ((target.IgnoreCommands ?? Config.IgnoreCommands) && (target.OnlyShowCommands ?? Config.OnlyShowCommands))
            {
                Logger.LogWarning("Ignoring ChatRelay target {TargetIp}:{TargetPort} because both command filters are enabled.", target.TargetIp, target.TargetPort);
                continue;
            }

            var hasSecureKey = ChatRelaySecureProtocol.TryParseKey(target.KeyBase64, out var validatedKey);
            if (hasSecureKey)
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(validatedKey);
            if (!string.IsNullOrWhiteSpace(target.KeyBase64) && !hasSecureKey)
            {
                Logger.LogWarning("Ignoring ChatRelay target {TargetIp}:{TargetPort}: its configured secure KeyBase64 is invalid; refusing plaintext downgrade.",
                    target.TargetIp, target.TargetPort);
                continue;
            }
            if (!hasSecureKey)
            {
                var legacyOnly = target.AllowLegacyPlaintext.HasValue || !string.IsNullOrWhiteSpace(target.SharedToken);
                Logger.LogWarning(
                    "Ignoring ChatRelay target {TargetIp}:{TargetPort}: {ConfigurationError}",
                    target.TargetIp, target.TargetPort,
                    legacyOnly
                        ? "its saved plaintext settings are no longer supported; configure a matching canonical base64 32-byte KeyBase64."
                        : "configure a matching canonical base64 32-byte KeyBase64; plaintext transport is not supported.");
                continue;
            }
            if (target.AllowLegacyPlaintext.HasValue || !string.IsNullOrWhiteSpace(target.SharedToken))
                Logger.LogWarning("ChatRelay target {TargetIp}:{TargetPort} contains obsolete plaintext settings; they are ignored and only authenticated UDP v1 will be sent.",
                    target.TargetIp, target.TargetPort);

            try
            {
                var addresses = Dns.GetHostAddresses(target.TargetIp);
                var address = addresses.FirstOrDefault(candidate => candidate.AddressFamily == AddressFamily.InterNetwork)
                    ?? addresses.FirstOrDefault();
                if (address is null)
                {
                    Logger.LogWarning("Ignoring ChatRelay target {TargetIp}:{TargetPort} because its host resolved to no addresses.", target.TargetIp, target.TargetPort);
                    continue;
                }

                var endPoint = new IPEndPoint(address, target.TargetPort);
                validTargets.Add(new ChatRelayDestination(target, endPoint, new UdpClient(address.AddressFamily)));
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                Logger.LogWarning(ex, "Ignoring ChatRelay target {TargetIp}:{TargetPort} because its host could not be resolved.", target.TargetIp, target.TargetPort);
            }
        }

        return validTargets;
    }

    private async Task SendUdpPayloadAsync(ChatRelayDestination target, string playerName, string message, string team, ulong steamId)
    {
        try
        {
            await ChatRelayUdpSender.SendAsync(target.UdpClient, target.EndPoint, target.Target,
                playerName, message, team, steamId);
            Logger.LogDebug("ChatRelay authenticated UDP v1 datagram sent to {TargetIp}:{TargetPort}.",
                target.Target.TargetIp, target.Target.TargetPort);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ChatRelay UDP send failed.");
        }
    }

    public override void Unload(bool hotReload)
    {
        foreach (var target in configuredTargets)
            target.Dispose();

        configuredTargets = [];
    }
}
