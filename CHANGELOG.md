# Changelog

## Unreleased

- Fixed startup after an unclean shutdown when the hidden `active-session.lock` marker from the previous session could not be overwritten by .NET Framework.
- Added a Windows regression test for refreshing hidden and read-only crash-session markers.

## 6.0.5

- Fixed XHTTP/V2Ray sites being sent outside the tunnel when poisoned Windows or ISP DNS resolved a blocked domain to a private IP address.
- Xray now preserves domain names for proxy-side resolution while keeping explicit private IP destinations available to the local network.
- Prevents the misleading `local-socks -> direct` route for domain-based browser requests such as YouTube.

## 6.0.4

- Fixed valid connections being torn down when one HTTP connectivity-check service closed its response.
- The final integrated-routing gate now validates the real SOCKS5 chain instead of depending on Cloudflare, GStatic, or Microsoft probe responses.
- Keeps the upstream V2Ray/Xray connection and TUN active when the proxy chain is ready, while preserving strict provider-level internet validation.

## 6.0.3

- Fixed .NET Framework compilation error CS1985 by moving asynchronous routing cleanup outside the catch clause.
- Added a compatibility check that rejects await expressions inside catch/finally blocks before packaging.

## 6.0.2

- Fixed false Connected states by verifying a real website through the final mixed browser-routing port before reporting success.
- Replaced the domain-bootstrapped routed DNS endpoint with IP-addressed DoH to prevent Windows/ISP DNS poisoning and circular lookup failures.
- Prioritized explicit mixed-proxy traffic over process bypass rules so connection tests cannot accidentally use the direct Windows connection.
- Stops TUN and System Proxy while an upstream provider reconnects, then rebuilds and verifies routing after recovery.
- Converts integrated-routing startup or verification failures into a visible Connection error instead of leaving Windows on a dead proxy.

## 6.0.1

- Replaced page-level Connect/Disconnect controls with one global minimalist Power toggle in the header.
- Profile double-click now safely stops the active SSH, V2Ray, Xray, or Psiphon provider before activating and connecting the new profile.
- Kept the Connections page open while switching or connecting profiles.
- Hardened provider shutdown so any stale provider process is stopped before a new connection starts.

## 6.0.0

- Renamed and fully rebranded the anonymous public project as Nivan Shield.
- Moved managed subscriptions to the top of V2Ray and removed the separate refresh/save steps from first use.
- Added one-action V2Ray selection and connection through buttons or profile double-click.
- Added XHTTP support through a bundled, hash-pinned official Xray-core executable.
- Added configurable and uniqueness-validated keyboard shortcuts.
- Added right-side Persian navigation, compact contextual guides, a new shield icon, and English as the default language.
- Added sorting by recommendation, latency, name, category, and protocol.
- Updated CI to publish separate Windows portable and source archives.

## 5.2.0

- Added fully offline QR-image import for V2Ray share links and HTTP/HTTPS subscriptions using an embedded, hash-pinned ZXing.Net decoder.
- Added extended multi-selection to connection profiles with visible Select all/Delete selected controls.
- Added profile shortcuts: Ctrl+A, Delete, Ctrl+D, Ctrl+N, Ctrl+I, and Ctrl+Shift+I.
- Added safe bulk deletion of profile credentials; deleting every profile returns the app to a blank SSH form.
- Removed all ready-to-use account/config defaults from the public source. Fresh installs contain no host, username, V2Ray config, or subscription.

## 5.1.1

- Added a one-click emergency reset that stops Nivan routing, clears Nivan-managed Windows LAN proxy values, and safely restores DNS changed by Nivan.
- Home SSH, V2Ray, and Psiphon buttons now select their provider before opening its page.
- Added a V2Ray-only saved-profile selector and a scoped V2Ray profile manager, so SSH accounts no longer appear in that workflow.
- Fixed duplicate config imports so they repair missing or non-portable DPAPI credentials instead of leaving an unusable profile.
- Replaced the generic invalid-profile warning with the exact validation or credential recovery reason.

## 5.1.0

- Added instant Persian/English switching from the top toolbar with persisted language selection.
- Added a runtime localization layer covering static pages, live connection states, dialogs, and tray commands.
- Replaced the Home active-profile dropdown with direct SSH, V2Ray, DNS, and Psiphon section buttons.
- Restored NekoRay-compatible Whole Windows behavior with TUN and System Proxy enabled together.
- Kept account, server, password, and profile editing available without removing existing user data.

## 5.0.1

- Reduced primary navigation to Home, Connections, DNS, and Settings; provider pages are now contextual.
- Added an active-profile selector and everyday routing/DNS controls directly to Home.
- Removed duplicate dashboard status panels and kept diagnostics and connection health one click away.
- Embedded the runtime XAML interface in the EXE to prevent mutable elevated UI loading.
- Forced a clean rebuild when upgrading from 5.0.0 so an older portable EXE cannot be launched accidentally.

## 5.0.0

- Added protected-browser, selected-app, whole-device TUN, and System Proxy routing modes.
- Added staged provider/endpoint/local-proxy/in-tunnel/routing diagnostics.
- Added a reversible DNS Center with popular Iranian and public DNS presets.
- Added external SOCKS5/HTTP/HTTPS proxy profiles.
- Added an isolated optional Psiphon provider using user-approved official files.
- Added a compiled-in bundled-core SHA-256 policy, disabled custom core execution, and added private runtime-file ACLs.
- Hardened subscription and updater downloads against redirects, oversized data, and private targets.

## 4.1.0

- Simplified the main navigation to Home, SSH, V2Ray, Servers, and Advanced.
- Added quick actions for SSH, direct V2Ray configs, and subscription links.
- Made the first imported V2Ray profile active automatically.
- Added real internet verification before a V2Ray connection becomes Connected.
- Fixed circular DNS resolution for domain-based V2Ray server addresses.
- Added clearer V2Ray connection status and errors on the main V2Ray page.
- Added compatibility for HTTP header transport in common VMess/VLESS links.
- Removed personal SSH defaults from the public source.
- Added GitHub build, contribution, security, and licensing files.

## 4.0.0

- Added connection health and speed tests.
- Added Smart Connect and automatic failover.
- Added managed subscriptions, split tunneling, crash recovery, and verified update downloads.
