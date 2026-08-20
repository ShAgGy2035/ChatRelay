using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace ChatRelay;

public sealed class ChatRelayConfig : BasePluginConfig
{
    [JsonPropertyName("IgnoreCommands")]
    public bool IgnoreCommands { get; set; } = true;

    [JsonPropertyName("OnlyShowCommands")]
    public bool OnlyShowCommands { get; set; } = false;

    [JsonPropertyName("TargetIp")]
    public string TargetIp { get; set; } = "127.0.0.1";

    [JsonPropertyName("TargetPort")]
    public int TargetPort { get; set; } = 90;

    [JsonPropertyName("SharedToken")]
    public string SharedToken { get; set; } = "";
}

public sealed class ChatRelayMessage
{
    public string PlayerName { get; set; } = string.Empty;
    public string SteamID { get; set; } = string.Empty;
    public string Team { get; set; } = "[ALL]";
    public string Message { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}

public sealed class ChatRelay : BasePlugin, IPluginConfig<ChatRelayConfig>
{
    public override string ModuleName => "ChatRelay UDP";
    public override string ModuleDescription => "Forwards CS2 player chat to a UDP listener";
    public override string ModuleAuthor => "ShAgGy";
    public override string ModuleVersion => "1.0.0";

    public ChatRelayConfig Config { get; set; } = new();
    private UdpClient? udpClient;

    public void OnConfigParsed(ChatRelayConfig config)
    {
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        udpClient = new UdpClient();
        Logger.LogInformation("ChatRelay UDP loaded. Target: {TargetIp}:{TargetPort}", Config.TargetIp, Config.TargetPort);
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
        if (Config.IgnoreCommands && startsWithCommand)
        {
            Logger.LogDebug("Chat event ignored: command filtering enabled.");
            return HookResult.Continue;
        }
        if (Config.OnlyShowCommands && !startsWithCommand)
        {
            Logger.LogDebug("Chat event ignored: OnlyShowCommands enabled.");
            return HookResult.Continue;
        }

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

        Logger.LogInformation("Forwarding chat event from {PlayerName} to {TargetIp}:{TargetPort}.", player.PlayerName, Config.TargetIp, Config.TargetPort);
        _ = SendUdpPayloadAsync(player.PlayerName, message, team, player.SteamID);
        return HookResult.Continue;
    }

    private async Task SendUdpPayloadAsync(string playerName, string message, string team, ulong steamId)
    {
        try
        {
            if (udpClient is null)
                return;
            if (string.IsNullOrWhiteSpace(Config.TargetIp) || Config.TargetPort is < 1 or > 65535)
            {
                Logger.LogWarning("ChatRelay has an invalid UDP target configuration.");
                return;
            }

            var payload = new ChatRelayMessage
            {
                PlayerName = playerName,
                SteamID = steamId.ToString(),
                Team = team,
                Message = message,
                Timestamp = DateTime.UtcNow.ToString("O"),
                Token = Config.SharedToken
            };
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            var sent = await udpClient.SendAsync(bytes, bytes.Length, Config.TargetIp, Config.TargetPort);
            Logger.LogInformation("ChatRelay UDP packet sent: {Bytes} bytes to {TargetIp}:{TargetPort}.", sent, Config.TargetIp, Config.TargetPort);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ChatRelay UDP send failed.");
        }
    }

    public override void Unload(bool hotReload)
    {
        udpClient?.Dispose();
        udpClient = null;
    }
}
