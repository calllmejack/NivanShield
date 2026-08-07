# Security policy and threat model

Do not publish live credentials, private keys, subscription URLs, `%LOCALAPPDATA%\NivanShield`, or unredacted logs in a public issue. Report a vulnerability privately to the repository owner with the affected version, reproduction steps, expected/actual behavior, and a redacted log.

## Security boundaries

Nivan Shield is a local connection orchestrator. It does not claim that a VPN provider, proxy owner, DNS service, SSH server, or imported subscription is trustworthy. Users remain responsible for choosing endpoints they trust.

The application:

- protects stored secrets with Windows DPAPI scoped to the current account;
- avoids command-shell interpolation for untrusted connection data;
- writes runtime configuration with exclusive access and a current-user ACL, then deletes it after use;
- handles SSH AskPass inside the already-running Nivan executable instead of launching a replaceable helper executable;
- embeds the runtime XAML interface into the compiled executable instead of loading mutable UI markup from an elevated install folder;
- verifies the bundled Neko core, Xray-core, and QR decoder against compiled-in fingerprints and `tools/integrity.sha256` before build and execution;
- disables custom sing-box executable loading in secure mode;
- requires publisher verification and SHA-256 pinning for a user-supplied Psiphon executable;
- limits imported text, links, profile counts, and network response sizes;
- disables HTTP redirects and rejects loopback/private download targets by default;
- launches only installed Microsoft Edge or Google Chrome for protected-browser mode;
- takes a restorable snapshot before changing adapter DNS settings;
- contains no telemetry, advertising SDK, remote control, miner, credential uploader, Telegram scraper, or automatic proxy discovery.

## Known limitations

- DPAPI does not protect secrets from malware already running as the same Windows user. Use Windows account protection and disk encryption.
- Whole-device TUN and DNS changes require an elevated process, increasing the importance of code review and verified releases.
- The current portable build elevates the entire desktop process. A public release should move privileged TUN, DNS, and proxy recovery operations into a narrowly scoped signed Windows service so the UI can run unelevated.
- The legacy SSH compatibility flow removes the saved host key and accepts a newly presented key. This preserves prior behavior but weakens protection against a man-in-the-middle attack. Fingerprint pinning is the recommended next security improvement.
- System Proxy affects only applications that honor Windows proxy settings. It is not a firewall kill switch.
- DNS presets can change availability or ownership. Test a provider before applying it and use Restore if results are unexpected.
- Psiphon support remains disabled until the user explicitly supplies approved official files; Nivan does not silently download them.

## Release checklist

1. Build through the Windows CI workflow from a clean tag.
2. Review every change to process launch, update, subscription, DNS, and credential code.
3. Regenerate `tools/integrity.sha256` only after verifying upstream artifacts and licenses.
4. Publish the release archive SHA-256 and, when available, sign the application binary.
5. Confirm that no user settings, logs, credentials, keys, or private subscription URLs are present.
6. Verify that the generated EXE contains `Nivan.Shield.MainWindow.xaml` as an embedded resource and does not depend on runtime XAML from the install folder.
