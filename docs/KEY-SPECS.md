## Key Historical Specs for MouseKeyProxy (preserved in full detail from prior plan)

## Protobuf Definitions (to surface / implement in Network/)
syntax = "proto3";
package mousekeyproxy.v1;

service MouseKeyProxy {
  rpc SendInput (SendInputRequest) returns (CommandResult);  // toggle proxy path
  rpc InjectInput (InjectInputRequest) returns (CommandResult);  // host->specific w/o focus
  rpc SetMousePosition (SetMousePositionRequest) returns (CommandResult);
  rpc LocateProcess (LocateProcessRequest) returns (LocateProcessResponse);  // name/PID -> hwnd tree
  rpc SetFocusByHwnd (SetFocusByHwndRequest) returns (CommandResult);  // focus + bring front by hwnd (new)
  // clipboard sync is real-time push on change
}

message SendInputRequest { repeated InputEvent events = 1; string target = 2; }  // target for multi
message InjectInputRequest { repeated InputEvent events = 1; repeated string remotes = 2; }  // specific remotes, no focus switch
message SetMousePositionRequest { string remote = 1; int32 display = 2; int32 x = 3; int32 y = 4; }  // host positions mouse (w/o focus)

message LocateProcessRequest { string name = 1; uint32 pid = 2; }  // by name or PID
message LocateProcessResponse { repeated HwndNode tree = 1; }  // hwnd tree if avail
message HwndNode { uint64 hwnd = 1; string title = 2; string class = 3; repeated HwndNode children = 4; }

message SetFocusByHwndRequest { string remote = 1; uint64 hwnd = 2; bool bringToFront = 3; }  // set focus + optional bring front by hwnd (new)
message InputEvent { InputKind kind=1; int32 vk=2; int32 scan=3; uint32 flags=4; int32 dx=5; int32 dy=6; uint64 time=7; }
enum InputKind { KEY_DOWN=0; KEY_UP=1; MOUSE_MOVE=2; MOUSE_DOWN=3; MOUSE_UP=4; ... }  // full incl media

## Requirements + Acceptance Criteria (FRs + tests)
FR-MKP-001 (Hotkey toggle only): Support configurable hotkey (default local C-A-F1, remote F2) to switch active without edge mouse move. AC: Hotkey switches focus+proxy dir on both; no auto edge cross; test with hooks disabled for edge.
FR-MKP-002 (Keyboard follows): Keyboard input proxies to current mouse focus machine. AC: All keys (incl media, win, modifiers) injected to active only; verified via SendInput + hook capture tests.
FR-MKP-003 (Full proxy): Proxy all KB funcs. AC: Test media keys, shortcuts, etc work on target as if local.
FR-MKP-004 (Real-time clipboard LIFO): Real-time sync + merge history as LIFO stack, persist encrypted. AC: Copy on A immediate on B; history LIFO (new top), max N, encrypted file, merge LIFO, viewable, survives restart.
FR-MKP-005 (gRPC + window/process): host gRPC inject/SetMousePos (display/pos), LocateProcess (name/PID -> hwnd tree), SetFocusByHwnd. AC: REPL pair, multi to host; calls succeed w/o toggle.
FR-MKP-006 (Setup/REPL/service): REPL tool installs service+fw (elevate pwsh), uninstall reverses; settings LocalAppData; workspace via director + reqs via grok-plugin; .NET10, git, nuke. AC: dotnet tool install registers service, REPL can pair/discover, service runs, settings persist encrypted, reqs queryable post reg.
Tests: unit for LIFO/encrypt, interop, gRPC; integration end-to-end on 2 Win11 as in verification.

## SVG Wireframes for Tray Icon (deliverable in Elaboration/early Construction)
Create in docs/wireframes/ (svg + png renders):
- 01-tray-icon-menu.svg: default tray icon (mouse+key symbol), right-click menu: "Toggle Active (F1)", "Start Mirror Mode", "Inject Text to Remote...", "Start/Stop Service", "Pair/Discover (REPL)", "Settings", "Exit".
- 02-inject-form.svg: modal/form "Inject to Remote": dropdown remotes (or selected), text area (multi-line), buttons Send/Cancel. Preview of target.
- 03-mirror-mode.svg: "Mirror Mode Active" indicator + list of selected remotes (checkboxes to toggle which receive mirror KB). "Stop Mirror" button.
- 04-status.svg: hover/click shows connection status (connected remotes, host/client role, last clip event).
Use simple SVG (rects, text, icons via paths or emoji placeholders). Match style of prior wireframes (e.g. aiUnit or FunWasHad). Render to png for reviews. These drive Tray UI impl + AC in reqs.

## Critical Implementation Details from Plan (all preserved)
- REPL: `dotnet tool install` installs tool only. `mkp service install` (elevated): deploys self-contained Service and Agent from bundled payloads/ in nupkg, ACLs on %ProgramData%\MouseKeyProxy\, EventLog, sc create, netsh, starts service, creates scheduled task "MouseKeyProxyTray" (ONLOGON /RU user), launches it.
- Visibility Gate: de-nested worktree at f:\github\MouseKeyProxy-Fresh, src/ dirty, `git diff --name-only 'src/'` shows M and "diff --git a/src/...", verify-goal.ps1 leaves exactly 4 files in scratch.
- Ownership: Service owns gRPC/networking/watchdog; Agent owns input (hooks, SendInput, ClipCursor, clipboard listener), tray UI, user secrets (DPAPI). 
- Seams: IInputInjector, IHotkeyMonitor, ICursorClip, etc in Common.
- etc. (all other details from full plan: ownership, seams, proto, tests, etc.)
