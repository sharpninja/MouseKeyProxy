# Functional Requirements (MCP Server)

## FR-HOTKEY-001 Hotkey toggle contracts

The system must expose hotkey monitor, cursor clip, ownership, and toggle state contracts before behavior is implemented.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Compile-time contracts exist for hotkey events, toggle state, cursor clipping, and input injection seams.
- [ ] Red tests assert desired hotkey toggle behavior without production logic that satisfies them.

## FR-MKP-005 gRPC advanced controls and lab topology

Host calls Inject/SetMousePos/Locate/SetFocus; lab pair payton-legion2 + payton-desktop on port 50051.
Scope: layer-1+

## FR-MKP-006 Setup REPL service lifecycle

REPL manages pairing, settings, explicit service lifecycle, LocalAppData, v0.5.0 release with lab install scripts.
Scope: layer-1+

## FR-OWNERSHIP-001 Ownership boundary contracts

The system must define ownership policy boundaries that separate agent rights from service rights.
Scope: layer-1+
**Acceptance Criteria:**
- [ ] Policy seams compile for agent, service, and none ownership states.
- [ ] Red tests assert service rejection and agent access rules using NSubstitute seams.

