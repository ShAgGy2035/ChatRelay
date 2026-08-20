---

# ChatRelay UDP v1.0.0

ChatRelay UDP is a CounterStrikeSharp plugin designed to seamlessly forward Counter-Strike 2 (CS2) player chat messages to a companion desktop application via UDP.

## Features & Capabilities

* **Real-Time Chat Forwarding:** Instantly captures and transmits in-game chat messages from your CS2 server to an external listener.
* **Shared-Token Authentication:** Secures your UDP packet stream using token validation between the plugin and the desktop application to prevent unauthorized or forged packets.
* **Secure Storage:** Integrates Windows DPAPI for encrypted token storage within the desktop application.
* **Message Filtering & Controls:** Easily toggle command filtering (`IgnoreCommands` and `OnlyShowCommands`) directly via configuration.
* **Enhanced Diagnostics:** Provides robust error logging, packet diagnostics, and asynchronous UDP message transmission for stable performance.

## Requirements

* CounterStrikeSharp
* CounterStrikeSharp API 1.0.372
* .NET 10.0
* A desktop application or service listening for UDP packets

## Installation

1. Download `ChatRelay.dll` from the release assets.
2. Copy it to your server directory:
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
  "TargetIp": "YOUR_PUBLIC_IP_OR_DNS",
  "TargetPort": 90,
  "SharedToken": "REPLACE_WITH_A_LONG_RANDOM_TOKEN"
}

```

> **Note:** `TargetIp` must point to the public IP address or DNS name of the computer running the desktop listener. Do not use a private address such as `192.168.x.x` when the game server is hosted externally.

## Desktop Application Setup for Authentication

If you are using the companion desktop app, configure your matching token under:
`Settings > ChatRelay Security > Shared Token`

> **Important:** Ensure you use the exact same long, random value in both the plugin configuration and the desktop app.

## Router and Firewall Setup

Forward the configured UDP port from your router to the computer running the desktop application.

* **Example:** `UDP port 90 -> 192.168.1.21:90`

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
* Confirm `TargetIp`, `TargetPort`, and `SharedToken` are correct.
* Confirm the desktop chat listener is enabled.
* Use the desktop app's local UDP test first.
* Check Windows Firewall and router UDP forwarding.
* Enable **Debug Mode** in the desktop app to inspect listener and packet activity.
* Check the ChatRelay server log for chat filtering and UDP send errors.

## Security Notice

While UDP is inherently connectionless, v1.0.0 incorporates shared-token authentication to reject unauthenticated or malformed packets. For maximum deployment safety, restrict the forwarded port using strict firewall rules or route traffic through a VPN/private network.

## Compatibility

* **Target Framework:** .NET 10.0
* **API Dependency:** `CounterStrikeSharp.API` 1.0.372
