namespace MouseKeyProxy.Compliance.Tests;

public class TransitionReleaseTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    [Trait("Category", "ReleaseContract")]
    public void TEST_MKP_013_Release_Scripts_And_Version_0_5_0_Present()
    {
        var publish = Path.Combine(RepoRoot, "scripts", "publish-release.ps1");
        var e2e = Path.Combine(RepoRoot, "scripts", "run-transition-e2e.ps1");
        var soak = Path.Combine(RepoRoot, "scripts", "run-soak.ps1");
        Assert.True(File.Exists(publish), "scripts/publish-release.ps1 missing");
        Assert.True(File.Exists(e2e), "scripts/run-transition-e2e.ps1 missing");
        Assert.True(File.Exists(soak), "scripts/run-soak.ps1 missing");

        var publishText = File.ReadAllText(publish);
        Assert.Contains("0.5.0", publishText);
        Assert.Contains("nuget push", publishText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("git tag", publishText, StringComparison.OrdinalIgnoreCase);

        var e2eText = File.ReadAllText(e2e);
        Assert.Contains("gsudo", e2eText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mkp-e2e-elev.ps1", e2eText);

        var props = File.ReadAllText(Path.Combine(RepoRoot, "Directory.Build.props"));
        Assert.Contains("0.5.0", props);
    }

    [Fact]
    [Trait("Category", "TwoMachineE2E")]
    public void TEST_MKP_014_Transition_E2E_Script_Defines_Lab_Gate()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "run-transition-e2e.ps1"));
        Assert.Contains("payton-legion2", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("payton-desktop", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("receipts-transition-e2e", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("toggle", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SMOKE: PARTIAL", text);
        Assert.DoesNotContain("REMOTE: SKIPPED", text);

        var lab = File.ReadAllText(Path.Combine(RepoRoot, "src", "MouseKeyProxy.Common", "LabTopology.cs"));
        Assert.Contains("payton-legion2", lab, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("payton-desktop", lab, StringComparison.OrdinalIgnoreCase);
    }
}