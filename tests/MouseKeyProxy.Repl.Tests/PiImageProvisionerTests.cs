using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MouseKeyProxy.Repl;
using Xunit;

namespace MouseKeyProxy.Repl.Tests;

/// <summary>
/// FR-MKP-012: Verifies the bundled Pi provisioning flow (mkp pi provision): image download,
/// SHA-256 verification, skip/force staging, manifest emission, Rufus argument construction, and
/// the bundled-Rufus launch seam. Uses a stub <see cref="HttpMessageHandler"/> and a spy
/// <see cref="IRufusLauncher"/>; no real network or process launch occurs.
/// </summary>
public class PiImageProvisionerTests : IDisposable
{
    private readonly string _stageRoot = Path.Combine(Path.GetTempPath(), "mkp-pi-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Removes the per-test staging directory.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_stageRoot))
            {
                Directory.Delete(_stageRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
    }

    private static string Sha256Hex(byte[] data)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
    }

    private PiProvisionOptions OptionsFor(byte[] payload, bool force = false, bool launch = false)
        => new()
        {
            ImageUrl = new Uri("https://example.test/raspios-lite-arm64.img.xz"),
            ExpectedSha256 = Sha256Hex(payload),
            StageRoot = _stageRoot,
            Profile = "default",
            Force = force,
            LaunchRufus = launch
        };

    /// <summary>Downloads the image when absent, verifies the checksum, and writes image + manifest.</summary>
    [Fact]
    [Trait("Category", "PiProvision")]
    public async Task StageImageAsync_DownloadsWhenMissing_VerifiesSha_WritesImageAndManifest()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var handler = new StubHttpMessageHandler(payload);
        var provisioner = new PiImageProvisioner(new HttpClient(handler));

        var imagePath = await provisioner.StageImageAsync(OptionsFor(payload), TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.CallCount);
        Assert.True(File.Exists(imagePath));
        Assert.Equal("raspios-lite-arm64.img.xz", Path.GetFileName(imagePath));
        Assert.Equal(payload, await File.ReadAllBytesAsync(imagePath, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(_stageRoot, "manifest.json")));
        Assert.False(File.Exists(imagePath + ".download"));
    }

    /// <summary>Does not re-download when a staged copy already exists and Force is false.</summary>
    [Fact]
    [Trait("Category", "PiProvision")]
    public async Task StageImageAsync_SkipsDownload_WhenPresentAndNotForced()
    {
        var payload = new byte[] { 9, 9, 9 };
        Directory.CreateDirectory(_stageRoot);
        var existing = Path.Combine(_stageRoot, "raspios-lite-arm64.img.xz");
        await File.WriteAllBytesAsync(existing, payload, TestContext.Current.CancellationToken);

        var handler = new StubHttpMessageHandler(payload) { ThrowIfCalled = true };
        var provisioner = new PiImageProvisioner(new HttpClient(handler));

        var imagePath = await provisioner.StageImageAsync(OptionsFor(payload), TestContext.Current.CancellationToken);

        Assert.Equal(0, handler.CallCount);
        Assert.Equal(existing, imagePath);
    }

    /// <summary>Re-downloads when Force is true even if a staged copy exists.</summary>
    [Fact]
    [Trait("Category", "PiProvision")]
    public async Task StageImageAsync_ReDownloads_WhenForced()
    {
        var payload = new byte[] { 4, 5, 6 };
        Directory.CreateDirectory(_stageRoot);
        await File.WriteAllBytesAsync(Path.Combine(_stageRoot, "raspios-lite-arm64.img.xz"), new byte[] { 0 }, TestContext.Current.CancellationToken);

        var handler = new StubHttpMessageHandler(payload);
        var provisioner = new PiImageProvisioner(new HttpClient(handler));

        var imagePath = await provisioner.StageImageAsync(OptionsFor(payload, force: true), TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(payload, await File.ReadAllBytesAsync(imagePath, TestContext.Current.CancellationToken));
    }

    /// <summary>Throws on SHA-256 mismatch and does not leave a staged image or download temp file.</summary>
    [Fact]
    [Trait("Category", "PiProvision")]
    public async Task StageImageAsync_ShaMismatch_Throws_AndLeavesNoImage()
    {
        var payload = new byte[] { 7, 7, 7 };
        var handler = new StubHttpMessageHandler(payload);
        var provisioner = new PiImageProvisioner(new HttpClient(handler));
        var options = new PiProvisionOptions
        {
            ImageUrl = new Uri("https://example.test/raspios-lite-arm64.img.xz"),
            ExpectedSha256 = Sha256Hex(new byte[] { 0, 0, 0 }),
            StageRoot = _stageRoot,
            LaunchRufus = false
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provisioner.StageImageAsync(options, TestContext.Current.CancellationToken));

        Assert.False(File.Exists(Path.Combine(_stageRoot, "raspios-lite-arm64.img.xz")));
        Assert.False(File.Exists(Path.Combine(_stageRoot, "raspios-lite-arm64.img.xz.download")));
    }

    /// <summary>Builds the exact Rufus argument list expected by the custom rufus-mkp CLI.</summary>
    [Fact]
    [Trait("Category", "PiProvision")]
    public void BuildLaunchArguments_ProducesGuiIsoAndProfileArgs()
    {
        IReadOnlyList<string> args = PiImageProvisioner.BuildLaunchArguments(@"C:\stage\raspios.img.xz", "lab");

        Assert.Equal(new[] { "--gui", "--iso", @"C:\stage\raspios.img.xz", "--mkp-pi-profile=lab" }, args);
    }

    /// <summary>Quotes the image path in the launch command line so paths with spaces are safe.</summary>
    [Fact]
    [Trait("Category", "PiProvision")]
    public void BuildLaunchCommandLine_QuotesImagePath()
    {
        var cmd = PiImageProvisioner.BuildLaunchCommandLine(@"C:\my stage\raspios.img.xz", "default");

        Assert.Equal("--gui --iso \"C:\\my stage\\raspios.img.xz\" --mkp-pi-profile=default", cmd);
    }

    /// <summary>Stages the image then launches the bundled Rufus with the staged path and profile.</summary>
    [Fact]
    [Trait("Category", "PiProvision")]
    public async Task ProvisionAsync_StagesThenLaunchesBundledRufus()
    {
        var payload = new byte[] { 1, 1, 2, 3 };
        Directory.CreateDirectory(_stageRoot);
        var fakeRufus = Path.Combine(_stageRoot, "rufus.exe");
        await File.WriteAllTextAsync(fakeRufus, "stub", TestContext.Current.CancellationToken);

        var handler = new StubHttpMessageHandler(payload);
        var launcher = new SpyRufusLauncher();
        var provisioner = new PiImageProvisioner(new HttpClient(handler), launcher, fakeRufus);

        var result = await provisioner.ProvisionAsync(OptionsFor(payload, launch: true), TestContext.Current.CancellationToken);

        Assert.True(result.Ok);
        Assert.Equal(fakeRufus, launcher.ExePath);
        Assert.Contains("--gui --iso", launcher.Arguments);
        Assert.Contains("raspios-lite-arm64.img.xz", launcher.Arguments);
        Assert.Contains("--mkp-pi-profile=default", launcher.Arguments);
        Assert.Equal(result.ImagePath, Path.Combine(_stageRoot, "raspios-lite-arm64.img.xz"));
    }

    /// <summary>Returns Ok=false and does not launch when the bundled Rufus executable is missing.</summary>
    [Fact]
    [Trait("Category", "PiProvision")]
    public async Task ProvisionAsync_ReturnsFailure_WhenBundledRufusMissing()
    {
        var payload = new byte[] { 5, 5 };
        var handler = new StubHttpMessageHandler(payload);
        var launcher = new SpyRufusLauncher();
        var missingRufus = Path.Combine(_stageRoot, "does-not-exist", "rufus.exe");
        var provisioner = new PiImageProvisioner(new HttpClient(handler), launcher, missingRufus);

        var result = await provisioner.ProvisionAsync(OptionsFor(payload, launch: true), TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Null(launcher.ExePath);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Program.cs exposes the pi provision command and its help text.</summary>
    [Fact]
    [Trait("Category", "PiProvision")]
    public void Program_Exposes_PiProvision_Command()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(repoRoot, "src", "MouseKeyProxy.Repl", "Program.cs"));

        Assert.Contains("case \"pi\":", source, StringComparison.Ordinal);
        Assert.Contains("mkp pi provision", source, StringComparison.Ordinal);
        Assert.Contains("PiImageProvisioner", source, StringComparison.Ordinal);
    }

    /// <summary>The Repl project bundles the rufus.exe payload for both local build and the packed tool.</summary>
    [Fact]
    [Trait("Category", "PiProvision")]
    public void ReplProject_Bundles_Rufus_Payload()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var csproj = File.ReadAllText(Path.Combine(repoRoot, "src", "MouseKeyProxy.Repl", "MouseKeyProxy.Repl.csproj"));

        Assert.Contains(@"assets\rufus\rufus.exe", csproj, StringComparison.Ordinal);
        Assert.Contains(@"payloads\rufus", csproj, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(repoRoot, "assets", "rufus", "rufus.exe")));
    }

    /// <summary>Stub HTTP handler returning fixed bytes; can assert it was or was not invoked.</summary>
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;

        public StubHttpMessageHandler(byte[] payload) => _payload = payload;

        public int CallCount { get; private set; }

        public bool ThrowIfCalled { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (ThrowIfCalled)
            {
                throw new InvalidOperationException("HTTP download should not have been attempted.");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload)
            });
        }
    }

    /// <summary>Spy launcher that records the last launch instead of starting a process.</summary>
    private sealed class SpyRufusLauncher : IRufusLauncher
    {
        public string? ExePath { get; private set; }

        public string Arguments { get; private set; } = string.Empty;

        public void Launch(string exePath, string arguments)
        {
            ExePath = exePath;
            Arguments = arguments;
        }
    }
}
