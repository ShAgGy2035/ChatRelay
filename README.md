# ChatRelay UDP

Development version: 1.0.1

ChatRelay forwards CS2 player chat to a desktop listener such as [Universal](https://github.com/ShAgGy2035/Ultimate-RCON-Server-Tool). Install the plugin on the game server, then configure the matching destination and encryption key in both applications.

**New installation?** Follow Installation, Desktop Application Setup, and Router and Firewall Setup, then [verify delivery](#verify-chat-delivery). **Upgrading from plaintext?** Read the [migration warning](#legacy-plaintext-migration) before replacing either end.

## Contents

- [Features & Capabilities](#features--capabilities)
- [Requirements](#requirements)
- [Download](#download)
- [Installation](#installation)
- [Desktop Application Setup](#desktop-application-setup)
- [Router and Firewall Setup](#router-and-firewall-setup)
- [Verify Chat Delivery](#verify-chat-delivery)
- [Configuration](#configuration)
  - [Message Filters](#message-filters)
- [Security Notice](#security-notice)
- [Troubleshooting](#troubleshooting)
- [Wire Protocol](#wire-protocol)
- [Building a Local Package](#building-a-local-package)
- [Compatibility](#compatibility)

## Features & Capabilities

* **Live Chat Forwarding:** Sends all-chat and team-chat messages to an external listener, including their player name, Steam ID, team label, and UTC timestamp.
* **Encrypted Messages:** Messages are protected against reading and alteration; the shared key is never sent with them.
* **Duplicate Protection:** Universal rejects previously received messages and messages outside the allowed time window.
* **Multiple Destinations:** Forward chat to as many as 10 independent IP/DNS and UDP port combinations.
* **Per-Target Keys:** Configure a separate 32-byte encryption key for each destination.
* **Message Filtering & Controls:** Set global command filters and optionally override them per target.
* **Diagnostics:** Reports invalid destinations and send errors in server logs; Universal provides listener diagnostics and a local UDP test.

ChatRelay forwards game chat to the listener; it is not a two-way chat bridge. Universal's outbound server messages use RCON separately. UDP delivery is not guaranteed, even when a send reports success.

## Requirements

* A CS2 dedicated server with Metamod and CounterStrikeSharp supporting .NET 10.
* CounterStrikeSharp API 1.0.372 or a compatible runtime.
* Universal 1.0.6 or another listener supporting ChatRelay's encrypted version-1 message format.

## Download

Download the latest installation ZIP from [GitHub Releases](https://github.com/ShAgGy2035/ChatRelay/releases). Compiled binaries are distributed through releases rather than the repository's Code tab.

Version 1.0.1 is not yet published. See the [1.0.1 release notes](Release%20Notes-1.0.1.md) for upcoming changes; older unencrypted releases are not compatible with the secure-only listener.

## Installation

1. Download the latest `ChatRelay` ZIP from [GitHub Releases](https://github.com/ShAgGy2035/ChatRelay/releases).
2. Extract `ChatRelay.dll` and copy it to your server directory:
`game/csgo/addons/counterstrikesharp/plugins/ChatRelay/ChatRelay.dll`
3. Start or reload the plugin.
4. Locate the generated configuration file:
`game/csgo/addons/counterstrikesharp/configs/plugins/ChatRelay/ChatRelay.json`
5. Continue with the listener and destination setup below. A loaded plugin with no valid destinations cannot forward messages.

Back up existing configuration before upgrading. Keep it outside public downloads and source control because it contains encryption keys.

## Desktop Application Setup

1. In Universal, select the server and open **Edit server > Integrations**.
2. Use **Generate** to create a cryptographically random 32-byte key, then save the profile. Universal protects the saved key using its credential-protection mechanism.
3. Open Universal's **Chat** tab, select the listener's network adapter, and set its UDP port (`1024`-`65535`; default `9090`).
4. Use **Copy ChatRelay target JSON** and put the generated entry in the plugin configuration's `Targets` list. Keep the surrounding JSON structure shown under [Configuration](#configuration).
5. Check the copied `TargetIp`: it contains the selected adapter's local address. If the game server is outside that network, replace it in the plugin entry with the desktop network's reachable public IP/DNS or VPN address. Do not use the game server's own address as the destination.
6. Save valid JSON, then reload ChatRelay or restart the server.

Each listener needs the same key as its corresponding plugin target. Invalid or missing keys stop delivery rather than sending unencrypted messages. Never include a real key in screenshots, support reports, or source control.

## Router and Firewall Setup

The connection runs **from the game server to the desktop listener**, not the other way around. For an externally hosted game server, `TargetIp` normally needs the desktop network's public IP or DNS name. A private address works only when the server can route to it, for example on the same LAN or a suitable VPN.

If the listener is behind a router, forward the configured UDP port to that computer. For example:

```text
Public UDP 9090 -> desktop 192.168.1.21:9090
```

Allow the listener through the desktop firewall using appropriately restricted rules. In Universal, select the correct network adapter and matching UDP port, then enable the chat listener. Encryption does not create forwarding rules, change bind addresses, or bypass a firewall.

## Verify Chat Delivery

1. Confirm `ChatRelay` is loaded in CounterStrikeSharp and the startup log reports the expected number of valid targets.
2. In Universal, enable the listener for the configured profile and run its local UDP test.
3. Send an ordinary player-chat message from the game server, not a `!` or `/` command (commands are filtered by default).
4. Check that Universal displays the message, for example `[ALL] PlayerName: Hello`. Try team chat if you need to verify its label too.

The local UDP test checks the desktop listener; it does not prove that the remote server can reach it. Successful in-game delivery verifies the complete route and matching key. After changing profiles or keys, stop and re-enable Universal's listener. Keep both computers' clocks accurate.

## Configuration

Use [ChatRelay.example.json](ChatRelay.example.json) as a template. This example sends ordinary chat to one listener and only commands to a second:

```json
{
  "IgnoreCommands": true,
  "OnlyShowCommands": false,
  "Targets": [
    {
      "TargetIp": "YOUR_PUBLIC_IP_OR_DNS",
      "TargetPort": 9090,
      "KeyBase64": "PASTE_BASE64_OF_32_RANDOM_BYTES_HERE"
    },
    {
      "TargetIp": "SECOND_PUBLIC_IP_OR_DNS",
      "TargetPort": 9091,
      "KeyBase64": "PASTE_A_DIFFERENT_BASE64_32_BYTE_KEY_HERE",
      "IgnoreCommands": false,
      "OnlyShowCommands": true
    }
  ],
  "ConfigVersion": 1
}

```

Replace every address/key placeholder before use. The plugin accepts `TargetPort` from `1` to `65535`; Universal's listener uses `1024`-`65535`. Only the first 10 target entries are considered; additional entries are ignored. Reload the plugin after changing addresses, keys, or filters.

Each target uses a distinct 32-byte random key encoded as canonical base64. Prefer Universal's generator; if OpenSSL is available, `openssl rand -base64 32` is an alternative. Copy the same value to Universal's **Edit server > Integrations > Secure key**. Restrict access to the plugin configuration and store backups privately.

### Message Filters

Messages beginning with `!` or `/` are treated as commands. The top-level filter values are defaults; each target can override either value. Omitted overrides inherit the global setting.

| Effective `IgnoreCommands` | Effective `OnlyShowCommands` | Forwarded messages |
| --- | --- | --- |
| `true` | `false` | Ordinary chat only (default) |
| `false` | `false` | Chat and commands |
| `false` | `true` | Commands only |
| `true` | `true` | None; target is skipped with a warning |

## Security Notice

### Legacy plaintext migration

Legacy plaintext sending and receiving are removed. A target must have a matching canonical base64 32-byte `KeyBase64`; saved `SharedToken` and `AllowLegacyPlaintext` values, including those in the old root single-target settings, are recognized only to report a migration warning and are never used to send or accept packets. A legacy-only target is skipped with a safe warning that asks the operator to configure the secure key. If a valid key is also present, only authenticated UDP v1 is used and the obsolete plaintext settings are ignored.

V1 authenticates possession of the shared key, not the sender's network address or the real-world identity of a player. UDP is connectionless; keep existing firewall/router policy restrictive and use a trusted network or VPN when appropriate. This protocol does not change listener bind addresses, firewall policy, or forwarded ports.

## Troubleshooting

Work from the plugin to the listener instead of repeatedly changing keys or opening ports:

| Symptom | Check |
| --- | --- |
| Plugin does not load | Verify the DLL path, full compatible CounterStrikeSharp runtime, and its load errors. |
| Startup reports zero or fewer targets | Check valid JSON, DNS resolution, port range, keys, the first-10-target limit, and contradictory filters. Look for the target's warning in server logs. |
| Local UDP test fails | Confirm Universal's listener is enabled with the intended adapter, profile, port, and valid key. Resolve local errors before testing the remote path. |
| Local test works, game chat does not | Check that `TargetIp` points to the listener, UDP forwarding reaches the correct computer, firewall rules allow the server, and both ends use the same key. |
| Commands are missing | Default filters suppress `!` and `/` messages. Check global values and per-target overrides. |
| Packets arrive but messages are rejected | Check matching keys and clock accuracy. Universal rejects messages outside its two-minute time window and rejects duplicate nonces. |
| Delivery stopped after a profile/key change | Reload ChatRelay and stop/re-enable Universal's listener using the matching profile. |

Enable **Debug Mode** in Universal to inspect listener and packet activity, reproduce the issue, then turn it off. ChatRelay reports target-validation and UDP-send errors in the CounterStrikeSharp server log. Review ordinary logs for player information, addresses, and secrets before sharing.

## Wire Protocol

V1 uses a bounded binary envelope: `CRLY` magic, version `1`, algorithm `1` (AES-256-GCM), signed 64-bit UTC Unix-millisecond timestamp, 12-byte nonce, encrypted UTF-8 JSON chat payload, and a 16-byte authentication tag. The authenticated additional data is the fixed context `ChatRelay/udp/v1` followed by the complete header. No shared key or token appears in a v1 packet. Datagram size is capped at 8 KiB and decrypted JSON at 4 KiB.

The sender combines a cryptographically random 64-bit session prefix with a monotonically increasing 32-bit counter for nonces (one sender sequence per process; a process restart creates a new random prefix). Counter exhaustion fails rather than reusing a nonce. Prefix collisions across restarts are probabilistic, with a 2^-64 collision probability per pair of sessions.

Universal accepts v1 timestamps within ±120 seconds and stores accepted nonces before displaying a message. The per-profile replay journal is bounded to 16,384 entries and retains entries for up to four minutes to cover the full future-skew window. A missing, corrupt, or unwritable journal causes secure listener startup or packet acceptance to fail closed. Deleting the journal resets replay history; the timestamp window then bounds the replay exposure to at most two minutes. Clock accuracy and protecting the local settings directory are prerequisites.

The desktop app displays messages in a readable format such as:
`[ALL] PlayerName: Hello`

## Building a local package

Run `.\build-release.ps1` from PowerShell. It builds `bin\Release\ChatRelay.zip`, containing `ChatRelay/ChatRelay.dll`, and writes `ChatRelay.zip.sha256` beside it after closing the archive. Add `-Verbose` for checksum-generation diagnostics. Rebuild after any DLL change so the checksum always matches the final ZIP.

This does not upload, publish, or deploy. When publication is explicitly approved, include the matching checksum file and verify GitHub's release-asset SHA-256 digest against the final ZIP. Universal automatically uses that API digest, not the sidecar; absent digest metadata still requires manual integrity approval.

## Compatibility

* **Target Framework:** .NET 10.0
* **API Dependency:** `CounterStrikeSharp.API` 1.0.372
