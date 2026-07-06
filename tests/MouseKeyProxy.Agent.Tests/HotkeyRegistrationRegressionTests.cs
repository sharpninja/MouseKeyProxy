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

        Assert.Contains("ShowDashboardForm();", source, StringComparison.Ordinal);
        Assert.Contains("_tray.DoubleClick", source, StringComparison.Ordinal);
        Assert.Contains("_hotkey.RegisterForWindow(_hotkeyWindow.Handle", source, StringComparison.Ordinal);
        Assert.Contains("SetVisibleCore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("hiddenForm.Show()", source, StringComparison.Ordinal);
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
}
