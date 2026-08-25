---

# ChatRelay UDP v1.0.0

ChatRelay UDP is a CounterStrikeSharp plugin designed to seamlessly forward Counter-Strike 2 (CS2) player chat messages to a companion desktop application (Example: [Ultimate RCON Server Tool](https://github.com/ShAgGy2035/Ultimate-RCON-Server-Tool)) via UDP.

## Download

Download the latest installation ZIP from [GitHub Releases](https://github.com/ShAgGy2035/ChatRelay/releases). Compiled binaries are distributed through releases rather than the repository's Code tab.

## Features & Capabilities

* **Real-Time Chat Forwarding:** Instantly captures and transmits in-game chat messages from your CS2 server to an external listener.
* **Shared-Token Authentication:** Secures your UDP packet stream using token validation between the plugin and the desktop application to prevent unauthorized or forged packets.
* **Secure Storage:** Integrates Windows DPAPI for encrypted token storage within the desktop application.
* **Multiple Destinations:** Forward chat to as many as 10 independent IP/DNS and UDP port combinations.
* **Per-Target Authentication:** Configure a separate shared token for each destination.
* **Message Filtering & Controls:** Set global command filters and optionally override them per target.
* **Enhanced Diagnostics:** Provides robust error logging, packet diagnostics, and asynchronous UDP message transmission for stable performance.

## Requirements

* CounterStrikeSharp
* CounterStrikeSharp API 1.0.372
* .NET 10.0
* A desktop application or service listening for UDP packets

## Installation

1. Download the latest `ChatRelay` ZIP from [GitHub Releases](https://github.com/ShAgGy2035/ChatRelay/releases).
2. Extract `ChatRelay.dll` and copy it to your server directory:
`game/csgo/addons/counterstrikesharp/plugins/ChatRelay/ChatRelay.dll`
3. Start or reload the plugin.
4. Configure the generated ChatRelay configuration file:
`game/csgo/addons/counterstrikesharp/configs/plugins/ChatRelay/ChatRelay.json`

## Configuration

Example (`ChatRelay.json`):

```json
{
  "IgnoreCommands": true,
  "OnlyShowCommands": false,
  "Targets": [
    {
      "TargetIp": "YOUR_PUBLIC_IP_OR_DNS",
      "TargetPort": 9090,
      "SharedToken": "REPLACE_WITH_A_LONG_RANDOM_TOKEN"
    },
    {
      "TargetIp": "SECOND_PUBLIC_IP_OR_DNS",
      "TargetPort": 9091,
      "SharedToken": "USE_A_DIFFERENT_LONG_RANDOM_TOKEN",
      "IgnoreCommands": false,
      "OnlyShowCommands": true
    }
  ],
  "ConfigVersion": 1
}

```

The top-level `IgnoreCommands` and `OnlyShowCommands` values are defaults. A target can override either value; omitted overrides inherit the global setting. A target with both effective values set to `true` is ignored because it would accept no messages.

Each target may use a different host, port, and token. Up to 10 targets are used; additional entries are ignored.

> **Note:** Each `TargetIp` must point to the public IP address or DNS name of the computer running that desktop listener. Do not use a private address such as `192.168.x.x` when the game server is hosted externally.

## Desktop Application Setup for Authentication

If you are using the companion desktop app, configure your matching token under:
`Settings > ChatRelay Security > Shared Token`

> **Important:** Ensure you use the exact same long, random value in both the plugin configuration and the desktop app.

## Router and Firewall Setup

Forward the configured UDP port from your router to the computer running the desktop application.

* **Example:** `UDP port 9090 -> 192.168.1.21:9090`

The desktop app must also be configured to:

* Select the correct network adapter.
* Use the same UDP port.
* Enable the chat listener.

## Message Format

ChatRelay sends UTF-8 JSON messages containing:

* Player name
* Steam ID
* Team or all-chat label
* Message text
* UTC timestamp

The desktop app displays messages in a readable format such as:
`[ALL] PlayerName: Hello`

## Troubleshooting

* Confirm `ChatRelay` appears as loaded in the CounterStrikeSharp logs.
* Confirm every target's `TargetIp`, `TargetPort`, and `SharedToken` are correct.
* Ensure every target host resolves and the JSON has no trailing comma after the final target.
* Confirm the desktop chat listener is enabled.
* Use the desktop app's local UDP test first.
* Check Windows Firewall and router UDP forwarding.
* Enable **Debug Mode** in the desktop app to inspect listener and packet activity.
* Check the ChatRelay server log for chat filtering and UDP send errors.

## Security Notice

While UDP is inherently connectionless, ChatRelay incorporates shared-token authentication to reject unauthenticated or malformed packets. For maximum deployment safety, restrict the forwarded port using strict firewall rules or route traffic through a VPN/private network.

## Compatibility

* **Target Framework:** .NET 10.0
* **API Dependency:** `CounterStrikeSharp.API` 1.0.372
