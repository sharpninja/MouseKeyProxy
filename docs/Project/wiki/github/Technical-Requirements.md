# Technical Requirements (MCP Server)

## TR-MKP-ARCH-001

**Process ownership and local IPC** — Define clear ownership: service owns networking, pairing, persistence, watchdog. User-session tray/agent owns LL hooks, hotkeys, clipboard listener, ClipCursor, SendInput, focus ops. Explicit local IPC (named pipes or loopback gRPC) with auth, timeouts, reconnect.
Scope: layer-1+

## TR-MKP-CLIP-001

**Clipboard LIFO model and persistence** — Define entry schema (formats, data), dedupe, LIFO merge rules, loop prevention, supported content (CF_UNICODETEXT, HTML (CF_HTML), DIB/PNG images only; file drop deferred), privacy (Exclude... and CF_CLIPBOARD_VIEWER_IGNORE skipped), receive-time + seq ordering, numeric caps (max 50 entries; per-item 10MB; total 100MB), json persistence under %LOCALAPPDATA% (CurrentUser DPAPI), opt-out + clear-history.
Scope: layer-1+

## TR-MKP-INPUT-001

**Input support matrix and limits** — Document and enforce realistic keyboard/mouse support matrix. Exclude SAS/Ctrl+Alt+Del, secure desktop, lock/login screens. Define conditional support and failure modes.
Scope: layer-1+

## TR-MKP-RELI-001

**Event reliability and failsafes** — Sequence/ack, stuck key recovery, disconnect cleanup, emergency unclip hotkey (2s hard deadline), auto-release on exit, local-only fallback. Latency TR: p95 end-to-end < 25 ms on LAN (4-hop path measured in Elaboration).
Scope: layer-1+

## TR-MKP-REPL-001

**REPL tool and service lifecycle contract** — Explicit commands: mkp service install (payload copy, sc create, fw), uninstall (reverse), update. Elevation model, rollback, binary location separate from global tool.
Scope: layer-1+

## TR-MKP-SEC-001

**Pairing auth and RPC authorization** — Define discovery (UDP broadcast and/or mDNS LAN), pairing code/ToFU, secret/cert lifecycle, mTLS with pinned thumbprints (no bearer/HMAC fallback), per-RPC auth matrix (restrict high impact calls), negative tests. Bind policy, ProgramData ACLs, UPnP IGD excluded.
Scope: layer-1+

