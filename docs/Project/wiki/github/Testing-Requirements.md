# Testing Requirements (MCP Server)

## TEST-MKP

### TEST-MKP-001

Verify configurable hotkey toggles proxy without edge detection or ClipCursor violation.


### TEST-MKP-002

Mocks confirm hooks/ClipCursor/SendInput only attempted in user-session component; service component rejects direct input attempts.


### TEST-MKP-003

SendInput succeeds for supported keys; explicitly rejects or no-ops for SAS/secure desktop with logged failure, no hang.


### TEST-MKP-004

Copy on A appears on B as top of LIFO stack; persist encrypted; max cap enforced; restart loads.


### TEST-MKP-005

Unpaired peer, bad secret, revoked peer rejected before any input/clipboard RPC; auth matrix prevents unauthorized Locate/SetFocus.


### TEST-MKP-006

Concurrent copies produce correct LIFO order, deduped, no loops; binary + text formats preserved; DPAPI roundtrips; cap enforced.


### TEST-MKP-007

mkp service install registers, starts; uninstall stops, removes, reverses fw; partial failure rolls back.


### TEST-MKP-008

red = clip not released within 2s on crash/disconnect or stuck modifier; green = ClipCursor release hard deadline 2s (safety), reconnect give-up 5s, modifier key-ups synthesized on every toggle transition (both directions); cmd = `dotnet test MouseKeyProxy.Agent.Tests --filter "Category=Failsafe"`
