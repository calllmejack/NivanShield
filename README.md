# Nivan Shield 6.0.5

Nivan Shield is an anonymous, open-source Windows client for SSH tunnels, V2Ray configs and subscriptions, external SOCKS/HTTP proxies, optional Psiphon, and reversible DNS switching.

> Mission: Making access to the free and open internet easier.

No author identity, personal server, username, subscription, private key, or credential is included. Fresh installs start with an empty SSH form.

## Everyday use

- Choose **SSH**, **V2Ray**, or **DNS** from Home.
- Use the single Power button in the top bar to connect or disconnect from any page.
- In V2Ray, add a subscription at the top of the page. Compatible configs are imported immediately; no separate Refresh or Save Profile step is required.
- Choose a config and press **Connect now**, or double-click a config in Connections. Double-click safely stops the previous provider, starts the selected profile, and keeps Connections open.
- Sort Connections by Recommended, Latency, Name, Category, or Protocol. Ctrl/Shift multi-selection and bulk deletion are supported.
- Import PNG/JPG QR files locally. A V2Ray share link or HTTP/HTTPS subscription is recognized automatically and is never uploaded to a QR service.
- Change every keyboard shortcut under Settings.

VMess, VLESS, Trojan, Shadowsocks, WebSocket, gRPC, HTTP Upgrade, HTTP and XHTTP transports are supported. XHTTP profiles use the bundled official Xray-core path; other compatible profiles use the bundled Neko/sing-box core.

## Traffic modes

Protected Browser affects only an isolated Edge/Chrome profile. System Proxy affects proxy-aware Windows apps. Selected Apps and Whole Windows use TUN; Whole Windows can enable TUN and System Proxy together. **Restore normal internet** stops app routing and reverses Nivan-managed proxy/DNS changes.

## Build and release

On Windows 10/11, run:

```bat
Build.cmd
```

The build validates the SHA-256 fingerprints of the bundled Neko core, Xray-core, and offline QR decoder before producing `NivanShield.exe`. The GitHub Actions workflow publishes two artifacts from a clean Windows build:

- `NivanShield-6.0.5-windows-x64.zip`
- `NivanShield-6.0.5-source.zip`

TUN and DNS changes require Administrator privileges. .NET Framework 4.7.2 or later and Windows OpenSSH Client are required.

## Security summary

- Credentials are protected with Windows DPAPI for the current account.
- Temporary core configurations receive a restrictive ACL and are deleted after use.
- Runtime XAML is embedded in the EXE.
- Bundled executable fingerprints are compiled in and checked against `tools/integrity.sha256`; custom core loading is disabled.
- Psiphon is never downloaded automatically. A user-selected official binary must pass publisher verification and is then hash-pinned.
- Subscription/update downloads are size-limited, redirect-free, and reject local/private targets by default.
- There is no telemetry, advertising, Telegram scraping, hidden proxy discovery, remote control, or executable command in imported configs.

See [README-FA.md](README-FA.md) for Persian instructions and [SECURITY.md](SECURITY.md) for limitations.

## License

Nivan Shield source is licensed under GNU GPL v3. Preserve all upstream licenses and notices when redistributing. ZXing.Net is under Apache-2.0 and Xray-core is under MPL-2.0; their notices are included under `tools`.
