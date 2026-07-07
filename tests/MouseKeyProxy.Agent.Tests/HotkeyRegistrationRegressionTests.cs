namespace MouseKeyProxy.Agent.Tests;

public class HotkeyRegistrationRegressionTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    [Trait("Category", "Hotkey")]
    public void Agent_Program_Dispatches_WM_HOTKEY_To_Toggle_Event()
    {
        var sourcePath = Path.Combine(RepoRoot, "src", "MouseKeyProxy.Agent", "Program.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("WM_HOTKEY", source, StringComparison.Ordinal);
        Assert.Contains("HotkeyMessageForm", source, StringComparison.Ordinal);
        Assert.Contains("RaiseToggle(\"Ctrl-Alt-F1\"", source, StringComparison.Ordinal);
        Assert.Contains("WndProc", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Hotkey")]
    public void Agent_Startup_Shows_Dashboard_Not_Blank_Hotkey_Window()
    {
        var sourcePath = Path.Combine(RepoRoot, "src", "MouseKeyProxy.Agent", "Program.cs");
        var source = File.ReadAllText(sourcePath);
        var dashboardStart = source.IndexOf("private static Form CreateDashboardForm", StringComparison.Ordinal);
        var dashboardEnd = source.IndexOf("private static Label AddDashboardRow", StringComparison.Ordinal);
        Assert.True(dashboardStart >= 0 && dashboardEnd > dashboardStart, "CreateDashboardForm helper was not found.");
        var dashboardHelper = source[dashboardStart..dashboardEnd];
        var buttonHelperStart = source.IndexOf("private static Button CreateDashboardButton", StringComparison.Ordinal);
        var buttonHelperEnd = source.IndexOf("private static Font CreateBoldMessageFont", StringComparison.Ordinal);
        Assert.True(buttonHelperStart >= 0 && buttonHelperEnd > buttonHelperStart, "CreateDashboardButton helper was not found.");
        var buttonHelper = source[buttonHelperStart..buttonHelperEnd];
        var openLogsStart = source.IndexOf("private static void OpenLogs", StringComparison.Ordinal);
        var openLogsEnd = source.IndexOf("private static void ExitApplication", StringComparison.Ordinal);
        Assert.True(openLogsStart >= 0 && openLogsEnd > openLogsStart, "OpenLogs helper was not found.");
        var openLogsHelper = source[openLogsStart..openLogsEnd];
        var remoteEndpointStart = source.IndexOf("private static string RemoteEndpointStatusText", StringComparison.Ordinal);
        var remoteEndpointEnd = source.IndexOf("private static bool EnsurePairedRemoteAction", StringComparison.Ordinal);
        Assert.True(remoteEndpointStart >= 0 && remoteEndpointEnd > remoteEndpointStart, "RemoteEndpointStatusText helper was not found.");
        var remoteEndpointHelper = source[remoteEndpointStart..remoteEndpointEnd];

        Assert.Contains("MinimumSize = new Size(811, 433)", dashboardHelper, StringComparison.Ordinal);
        Assert.Contains("Icon = LoadTrayIcon()", dashboardHelper, StringComparison.Ordinal);
        Assert.Contains("AutoSizeMode = AutoSizeMode.GrowAndShrink", dashboardHelper, StringComparison.Ordinal);
        Assert.Contains("WrapContents = false", dashboardHelper, StringComparison.Ordinal);
        Assert.Contains("Remote endpoint", dashboardHelper, StringComparison.Ordinal);
        Assert.Contains("RemoteEndpointStatusText", dashboardHelper, StringComparison.Ordinal);
        Assert.DoesNotContain("AddDashboardRow(layout, \"Service\"", dashboardHelper, StringComparison.Ordinal);
        Assert.Contains("RemoteConnectionState.NotPaired", remoteEndpointHelper, StringComparison.Ordinal);
        Assert.Contains("return \"None\"", remoteEndpointHelper, StringComparison.Ordinal);
        Assert.Contains("CreateDashboardButton", source, StringComparison.Ordinal);
        Assert.Contains("AutoSize = true", buttonHelper, StringComparison.Ordinal);
        Assert.Contains("AutoSizeMode = AutoSizeMode.GrowAndShrink", buttonHelper, StringComparison.Ordinal);
        Assert.Contains("Padding = new Padding(18, 8, 18, 8)", buttonHelper, StringComparison.Ordinal);
        Assert.Contains("SizeType.AutoSize", source, StringComparison.Ordinal);
        Assert.Contains("ShowDashboardForm();", source, StringComparison.Ordinal);
        Assert.Contains("_tray.DoubleClick", source, StringComparison.Ordinal);
        Assert.Contains("_hotkey.RegisterForWindow(_hotkeyWindow.Handle", source, StringComparison.Ordinal);
        Assert.Contains("SetVisibleCore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoEllipsis", buttonHelper, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateDashboardButton(\"Reconnect\",", source, StringComparison.Ordinal);
        Assert.DoesNotContain("hiddenForm.Show()", source, StringComparison.Ordinal);
        Assert.Contains("FileName = \"eventvwr.msc\"", openLogsHelper, StringComparison.Ordinal);
        Assert.Contains("Arguments = \"/c:MouseKeyProxy\"", openLogsHelper, StringComparison.Ordinal);
        Assert.DoesNotContain("Environment.SpecialFolder.LocalApplicationData", openLogsHelper, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.CreateDirectory", openLogsHelper, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "RemoteState")]
    public void Agent_Disables_Remote_Dependent_Actions_Until_Paired_And_Connected()
    {
        var sourcePath = Path.Combine(RepoRoot, "src", "MouseKeyProxy.Agent", "Program.cs");
        var source = File.ReadAllText(sourcePath);
        var mainMenuStart = source.IndexOf("var menu = new ContextMenuStrip", StringComparison.Ordinal);
        var mainMenuEnd = source.IndexOf("_tray.ContextMenuStrip = menu", StringComparison.Ordinal);
        Assert.True(mainMenuStart >= 0 && mainMenuEnd > mainMenuStart, "Main tray menu block was not found.");
        var mainMenu = source[mainMenuStart..mainMenuEnd];
        var toggleStart = source.IndexOf("private static void DoRealToggle", StringComparison.Ordinal);
        var toggleEnd = source.IndexOf("private static string ResolveRemoteGrpcUrl", StringComparison.Ordinal);
        Assert.True(toggleStart >= 0 && toggleEnd > toggleStart, "DoRealToggle helper was not found.");
        var toggleHelper = source[toggleStart..toggleEnd];
        var injectStart = source.IndexOf("private static void DoRealInject", StringComparison.Ordinal);
        Assert.True(injectStart >= 0, "DoRealInject helper was not found.");
        var injectHelper = source[injectStart..];

        Assert.Contains("RemoteConnectionState.NotPaired", source, StringComparison.Ordinal);
        Assert.Contains("RemoteConnectionState.NotConnected", source, StringComparison.Ordinal);
        Assert.Contains("Not paired", source, StringComparison.Ordinal);
        Assert.Contains("Not connected to a remote", source, StringComparison.Ordinal);
        Assert.Contains("AddConnectedRemoteMenuAction(menu, \"Toggle Active - Desktop Control (Ctrl-Alt-F1)\"", mainMenu, StringComparison.Ordinal);
        Assert.Contains("AddConnectedRemoteMenuAction(menu, \"Clipboard\"", mainMenu, StringComparison.Ordinal);
        Assert.Contains("AddConnectedRemoteMenuAction(menu, \"Inject Text to Remote...\"", mainMenu, StringComparison.Ordinal);
        Assert.Contains("AddPairedRemoteMenuAction(menu, \"Reconnect\"", mainMenu, StringComparison.Ordinal);
        Assert.Contains("menu.Items.Add(\"Emergency release\"", mainMenu, StringComparison.Ordinal);
        Assert.Contains("_primaryRemoteButton = CreateDashboardButton(\"Pair\")", source, StringComparison.Ordinal);
        Assert.Contains("UpdatePrimaryRemoteButton", source, StringComparison.Ordinal);
        Assert.Contains("_primaryRemoteButton.Text = \"Pair\"", source, StringComparison.Ordinal);
        Assert.Contains("_primaryRemoteButton.Text = \"Reconnect\"", source, StringComparison.Ordinal);
        Assert.Contains("ShowPairingForm();", source, StringComparison.Ordinal);
        Assert.Contains("binding.EnabledText} ({reason})", source, StringComparison.Ordinal);
        Assert.Contains("EnsureConnectedRemoteAction(\"Toggle Active - Desktop Control\")", toggleHelper, StringComparison.Ordinal);
        Assert.Contains("EnsureConnectedRemoteAction(\"Inject Text to Remote\")", injectHelper, StringComparison.Ordinal);
        Assert.Contains("EnsureConnectedRemoteAction(\"Clipboard\")", source, StringComparison.Ordinal);
        Assert.Contains("EnsurePairedRemoteAction(\"Reconnect\")", source, StringComparison.Ordinal);
        Assert.Contains("PerformEmergencyRelease(showUi: true, notifyPeer: true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("nullTransport", toggleHelper, StringComparison.Ordinal);
        Assert.DoesNotContain("[LOCAL fallback inject]", injectHelper, StringComparison.Ordinal);
        Assert.DoesNotContain("Start Mirror Mode", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowMirrorForm", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Pairing")]
    public void Agent_Accepts_Repl_Pairing_State_Notification()
    {
        var programPath = Path.Combine(RepoRoot, "src", "MouseKeyProxy.Agent", "Program.cs");
        var programSource = File.ReadAllText(programPath);
        var pipePath = Path.Combine(RepoRoot, "src", "MouseKeyProxy.Agent", "AgentControlPipeServer.cs");
        var pipeSource = File.ReadAllText(pipePath);

        Assert.Contains("AgentControlPipe.NotifyPairingState", pipeSource, StringComparison.Ordinal);
        Assert.Contains("NotifyPairingState", programSource, StringComparison.Ordinal);
        Assert.Contains("ApplyPairingState", programSource, StringComparison.Ordinal);
        Assert.Contains("PersistPairingState", programSource, StringComparison.Ordinal);
        Assert.Contains("LoadPersistedPairingState();", programSource, StringComparison.Ordinal);
        Assert.Contains("agent-pairing.json", programSource, StringComparison.Ordinal);
        Assert.Contains("AgentControlPipe.GetAgentStatus", pipeSource, StringComparison.Ordinal);
        Assert.Contains("GetAgentStatus", programSource, StringComparison.Ordinal);
        Assert.Contains("ForwardingActive = _forwarder?.IsActive ?? false", programSource, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Hotkey")]
    public void Win32HotkeyMonitor_Does_Not_Silently_Ignore_RegisterHotKey_Failure()
    {
        var sourcePath = Path.Combine(RepoRoot, "src", "MouseKeyProxy.Agent", "Win32SeamImpls.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("SetLastError = true", source, StringComparison.Ordinal);
        Assert.Contains("RegisterHotKey failed", source, StringComparison.Ordinal);
        Assert.Contains("Marshal.GetLastWin32Error()", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Hotkey")]
    public void Win32HotkeyMonitor_Installs_Keyboard_Hook_Fallback_For_Ctrl_Alt_F1()
    {
        var sourcePath = Path.Combine(RepoRoot, "src", "MouseKeyProxy.Agent", "Win32SeamImpls.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("WH_KEYBOARD_LL", source, StringComparison.Ordinal);
        Assert.Contains("SetWindowsHookEx(WH_KEYBOARD_LL", source, StringComparison.Ordinal);
        Assert.Contains("KeyboardHookCallback", source, StringComparison.Ordinal);
        Assert.Contains("IsCtrlAltF1", source, StringComparison.Ordinal);
        Assert.Contains("vk == VK_F1 && IsKeyDown(VK_CONTROL) && IsKeyDown(VK_MENU)", source, StringComparison.Ordinal);
        Assert.Contains("UnhookWindowsHookEx", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Hotkey")]
    public void Win32HotkeyMonitor_Debounces_Duplicate_Hook_And_WmHotkey_Dispatches()
    {
        var monitor = new MouseKeyProxy.Agent.Win32HotkeyMonitor();
        var dispatchCount = 0;
        monitor.ToggleRequested += (_, _) => dispatchCount++;

        monitor.RaiseToggle("Ctrl-Alt-F1", remote: false);
        monitor.RaiseToggle("Ctrl-Alt-F1", remote: false);

        Assert.Equal(1, dispatchCount);
    }

    [Fact]
    [Trait("Category", "Hotkey")]
    public void RemoteInputForwarder_Consumes_Modifier_Keys_While_Remote_Control_Is_Active()
    {
        var sourcePath = Path.Combine(RepoRoot, "src", "MouseKeyProxy.Agent", "RemoteInputForwarder.cs");
        var source = File.ReadAllText(sourcePath);
        var hookStart = source.IndexOf("private IntPtr KeyboardHookCallback", StringComparison.Ordinal);
        var hookEnd = source.IndexOf("private IntPtr MouseHookCallback", StringComparison.Ordinal);
        Assert.True(hookStart >= 0 && hookEnd > hookStart, "KeyboardHookCallback helper was not found.");
        var hook = source[hookStart..hookEnd];

        Assert.Contains("if (!IsToggleChord(data.vkCode))", hook, StringComparison.Ordinal);
        Assert.Contains("return new IntPtr(1);", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("IsLocalPassThroughKey", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CallNextHookEx(_keyboardHook, nCode, wParam, lParam) :", hook, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "EmergencyRelease")]
    public void Agent_Emergency_Release_Stops_Forwarder_And_Is_Available_Over_Control_Pipe()
    {
        var programPath = Path.Combine(RepoRoot, "src", "MouseKeyProxy.Agent", "Program.cs");
        var programSource = File.ReadAllText(programPath);
        var pipePath = Path.Combine(RepoRoot, "src", "MouseKeyProxy.Agent", "AgentControlPipeServer.cs");
        var pipeSource = File.ReadAllText(pipePath);

        Assert.Contains("AgentControlPipe.EmergencyRelease", pipeSource, StringComparison.Ordinal);
        Assert.Contains("ExecuteEmergencyReleaseCommand", programSource, StringComparison.Ordinal);
        Assert.Contains("PerformEmergencyRelease(showUi: false, notifyPeer: false", programSource, StringComparison.Ordinal);
        Assert.Contains("_forwarder?.Stop();", programSource, StringComparison.Ordinal);
        Assert.Contains("_clip?.Release();", programSource, StringComparison.Ordinal);
        Assert.Contains("_state?.Reset();", programSource, StringComparison.Ordinal);
        Assert.Contains("ForwardingActive = _forwarder?.IsActive ?? false", programSource, StringComparison.Ordinal);
    }
}
