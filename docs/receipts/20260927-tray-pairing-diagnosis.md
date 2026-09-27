# Tray pairing diagnosis

Recorded 2026-09-27 20:14 UTC by Codex.

- Published through `./build.ps1 PublishAgent --configuration Release`, exit 0, 0 errors and 59 CA1416 warnings. Build log `.mcpServer/20260927T195225Z-publish-agent.stdout.log`.
- Running published Agent PID 117124, interactive Session 1. Binary `output/payloads/agent/MouseKeyProxy.Agent.exe` SHA256 DB5B7218FE9F858A1B0CB3A7C257E9F151EA18B9BE18DF7C00105C0576697760, before certificate fix.
- `mkp settings show --json`: remotePeer 192.168.0.172; remoteGrpcUrl https://192.168.0.172:50051.
- `mkp agent status --json`: NotPaired, forwardingActive false. UI status remoteGrpcUrl None is state display, not settings value.
- `%LOCALAPPDATA%/MouseKeyProxy/logs/self-heal.log` at 2026-09-27 14:55:46 local: mTLS OK on alternate https://192.168.0.172:50051, recovered via alternate endpoint.
- `forwarder.log` at 14:55:58 local: inject TLS/auth failed; CryptographicException: A null or disposed certificate was present in CustomTrustStore.
- Source: Agent Program.cs ReloadPeerCredentialFromDisk disposes previous client and CA certificates; PairingClient.CreateAuthenticatedChannel retains caller client certificate and captures caller CA in validation callback. An active channel can therefore receive disposed objects.
- `ssh -vv -o BatchMode=yes -o ConnectTimeout=5 -o KexAlgorithms=curve25519-sha256 -o HostKeyAlias=192.168.1.133 -o StrictHostKeyChecking=yes mkp@192.168.0.172 hostname`: server ED25519 SHA256:qfiVcLhwD5e9iLsCg95ulKht/9WxccMV5ui2FSPeV2U; Host 192.168.1.133 is known and matches; found key in known_hosts line 20. SSH login permission denied. Device SSH identity matches former endpoint; physical board model not queried.

This receipt diagnoses the logged forwarding TLS defect. It does not claim completed pairing or a completed code fix.
