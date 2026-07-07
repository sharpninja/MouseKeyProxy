namespace MouseKeyProxy.Compliance.Tests;

public class TransitionReleaseTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    [Trait("Category", "ReleaseContract")]
    public void TEST_MKP_013_Release_Scripts_And_GitVersion_Nuke_Targets_Present()
    {
        var publish = Path.Combine(RepoRoot, "scripts", "publish-release.ps1");
        var e2e = Path.Combine(RepoRoot, "scripts", "run-transition-e2e.ps1");
        var soak = Path.Combine(RepoRoot, "scripts", "run-soak.ps1");
        Assert.True(File.Exists(publish), "scripts/publish-release.ps1 missing");
        Assert.True(File.Exists(e2e), "scripts/run-transition-e2e.ps1 missing");
        Assert.True(File.Exists(soak), "scripts/run-soak.ps1 missing");

        var publishText = File.ReadAllText(publish);
        Assert.Contains("dotnet-gitversion", publishText);
        Assert.Contains("PackRepl", publishText);
        Assert.Contains("PublishToolToNuGet", publishText);
        Assert.Contains("NUGET_API_KEY", publishText);
        Assert.DoesNotContain("0.5.0", publishText);

        var buildText = File.ReadAllText(Path.Combine(RepoRoot, "build", "Build.cs"));
        Assert.Contains("PublishToolToNuGet", buildText);
        Assert.Contains("NUGET_API_KEY", buildText);
        Assert.Contains("rev-list --tags --max-count=1", buildText);
        Assert.Contains("dotnet-gitversion", buildText);

        var props = File.ReadAllText(Path.Combine(RepoRoot, "Directory.Build.props"));
        Assert.Contains("GitVersion.MsBuild", props);
        Assert.DoesNotContain("<Version>0.5.0</Version>", props);
        Assert.DoesNotContain("<PackageVersion>0.5.0</PackageVersion>", props);
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
