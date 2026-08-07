Secure core policy
==================

Nivan Shield 5 does not load a custom sing-box executable from this folder.
V2Ray, external proxy, SSH routing and Psiphon routing use the reviewed
nekobox_core.exe shipped under tools/nekoray. Its SHA-256 fingerprint is
compiled into Nivan Shield and checked again through tools/integrity.sha256.

Developers who deliberately replace the core must review the upstream source,
licenses and release artifact, update the compiled fingerprint and publish a
new signed Nivan Shield release. End-user core replacement remains disabled.
