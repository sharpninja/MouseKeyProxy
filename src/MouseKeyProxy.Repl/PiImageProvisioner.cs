using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MouseKeyProxy.Repl;

/// <summary>
/// FR-MKP-012: Options controlling Raspberry Pi Zero 2 HID image staging and the
/// launch of the bundled "RUFUS For MouseKeyProxy" writer.
/// </summary>
public sealed class PiProvisionOptions
{
    /// <summary>Default Raspberry Pi OS Lite arm64 image URL (compressed .img.xz).</summary>
    public const string DefaultImageUrl = "https://downloads.raspberrypi.org/raspios_lite_arm64/images/raspios_lite_arm64-2026-06-19/2026-06-18-raspios-trixie-arm64-lite.img.xz";

    /// <summary>Expected SHA-256 of the default compressed image (lowercase hex).</summary>
    public const string DefaultSha256 = "acff736ca7945e3b305f07cda4abdb870910e12634991da69783611756e381b3";

    /// <summary>Default staging directory under the local application data folder.</summary>
    public static string DefaultStageRoot => Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "MouseKeyProxy",
        "pi-stage");

    /// <summary>Source URL of the Pi image to download.</summary>
    public System.Uri ImageUrl { get; init; } = new(DefaultImageUrl);

    /// <summary>Expected SHA-256 (lowercase hex) of the downloaded file, or null/empty to skip verification.</summary>
    public string? ExpectedSha256 { get; init; } = DefaultSha256;

    /// <summary>Directory the image is staged into.</summary>
    public string StageRoot { get; init; } = DefaultStageRoot;

    /// <summary>Named Pi HID profile passed to Rufus via --mkp-pi-profile.</summary>
    public string Profile { get; init; } = "default";

    /// <summary>When true, re-download even if a staged copy already exists.</summary>
    public bool Force { get; init; }

    /// <summary>When true (default), launch Rufus after staging; when false, stage only.</summary>
    public bool LaunchRufus { get; init; } = true;
}

/// <summary>FR-MKP-012: Result of a Pi image provisioning attempt.</summary>
/// <param name="Ok">True when staging (and launch, if requested) succeeded.</param>
/// <param name="Message">Human-readable status message.</param>
/// <param name="ImagePath">Full path of the staged image on disk.</param>
/// <param name="RufusPath">Full path of the bundled Rufus executable.</param>
/// <param name="LaunchArguments">Logical Rufus argument list used for launch and the manifest.</param>
public sealed record PiProvisionResult(
    bool Ok,
    string Message,
    string ImagePath,
    string RufusPath,
    IReadOnlyList<string> LaunchArguments);

/// <summary>FR-MKP-012: Abstraction over launching the bundled Rufus GUI (fire-and-forget).</summary>
public interface IRufusLauncher
{
    /// <summary>Starts Rufus at <paramref name="exePath"/> with <paramref name="arguments"/> without waiting for exit.</summary>
    /// <param name="exePath">Full path to the bundled rufus.exe.</param>
    /// <param name="arguments">Command-line argument string (image path already quoted).</param>
    void Launch(string exePath, string arguments);
}

/// <summary>FR-MKP-012: Default launcher that starts Rufus via the shell so its manifest can self-elevate.</summary>
public sealed class SystemRufusLauncher : IRufusLauncher
{
    /// <inheritdoc />
    public void Launch(string exePath, string arguments)
    {
        var psi = new ProcessStartInfo(exePath, arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? System.Environment.CurrentDirectory
        };
        Process.Start(psi);
    }
}

/// <summary>
/// FR-MKP-012: Downloads the Raspberry Pi Zero 2 HID image into a staging directory,
/// verifies its checksum, and launches the bundled "RUFUS For MouseKeyProxy" writer so the
/// operator can configure the Pi HID profile and write the image to SD media.
/// </summary>
public sealed class PiImageProvisioner
{
    private readonly HttpClient _http;
    private readonly IRufusLauncher _launcher;
    private readonly string? _rufusPathOverride;

    /// <summary>Creates a provisioner with optional injected HTTP client, launcher, and Rufus path (for tests).</summary>
    /// <param name="http">HTTP client used to download the image; a default client is created when null.</param>
    /// <param name="launcher">Rufus launcher seam; a <see cref="SystemRufusLauncher"/> is used when null.</param>
    /// <param name="rufusPathOverride">Overrides the resolved bundled Rufus path; used by tests.</param>
    public PiImageProvisioner(HttpClient? http = null, IRufusLauncher? launcher = null, string? rufusPathOverride = null)
    {
        _http = http ?? new HttpClient();
        _launcher = launcher ?? new SystemRufusLauncher();
        _rufusPathOverride = rufusPathOverride;
    }

    /// <summary>Full path of the bundled Rufus executable (payloads/rufus/rufus.exe next to the tool).</summary>
    public string RufusPath => _rufusPathOverride
        ?? Path.Combine(AppContext.BaseDirectory, "payloads", "rufus", "rufus.exe");

    /// <summary>Builds the logical Rufus argument list for the given image and profile.</summary>
    /// <param name="imagePath">Full path of the staged image.</param>
    /// <param name="profile">Named Pi HID profile.</param>
    /// <returns>Ordered argument list: --gui --iso &lt;path&gt; --mkp-pi-profile=&lt;profile&gt;.</returns>
    public static IReadOnlyList<string> BuildLaunchArguments(string imagePath, string profile) => new[]
    {
        "--gui",
        "--iso",
        imagePath,
        $"--mkp-pi-profile={profile}"
    };

    /// <summary>Builds the quoted command-line string handed to the launcher.</summary>
    /// <param name="imagePath">Full path of the staged image.</param>
    /// <param name="profile">Named Pi HID profile.</param>
    /// <returns>A command line with the image path quoted.</returns>
    public static string BuildLaunchCommandLine(string imagePath, string profile) =>
        $"--gui --iso \"{imagePath}\" --mkp-pi-profile={profile}";

    /// <summary>
    /// Downloads (unless already staged) and verifies the Pi image, writes a manifest, and
    /// returns the staged image path. Rufus accepts the compressed .img.xz directly and
    /// decompresses it during write, so no separate extraction step is required.
    /// </summary>
    /// <param name="options">Provisioning options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The full path of the staged image.</returns>
    public async Task<string> StageImageAsync(PiProvisionOptions options, CancellationToken ct = default)
    {
        Directory.CreateDirectory(options.StageRoot);

        var fileName = Path.GetFileName(options.ImageUrl.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "raspios-lite-arm64.img.xz";
        }

        var imagePath = Path.Combine(options.StageRoot, fileName);

        if (options.Force || !File.Exists(imagePath))
        {
            var tempPath = imagePath + ".download";
            try
            {
                using (var response = await _http
                    .GetAsync(options.ImageUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    await using var destination = File.Create(tempPath);
                    await source.CopyToAsync(destination, ct).ConfigureAwait(false);
                }

                if (!string.IsNullOrWhiteSpace(options.ExpectedSha256))
                {
                    var actual = await ComputeSha256Async(tempPath, ct).ConfigureAwait(false);
                    if (!string.Equals(actual, options.ExpectedSha256, System.StringComparison.OrdinalIgnoreCase))
                    {
                        throw new System.InvalidOperationException(
                            $"SHA-256 mismatch for {options.ImageUrl}. expected={options.ExpectedSha256} actual={actual}");
                    }
                }

                if (File.Exists(imagePath))
                {
                    File.Delete(imagePath);
                }

                File.Move(tempPath, imagePath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        WriteManifest(options, imagePath);
        return imagePath;
    }

    /// <summary>Stages the image and, unless disabled, launches the bundled Rufus writer.</summary>
    /// <param name="options">Provisioning options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The provisioning result.</returns>
    public async Task<PiProvisionResult> ProvisionAsync(PiProvisionOptions options, CancellationToken ct = default)
    {
        var imagePath = await StageImageAsync(options, ct).ConfigureAwait(false);
        var arguments = BuildLaunchArguments(imagePath, options.Profile);

        if (!options.LaunchRufus)
        {
            return new PiProvisionResult(true, "Image staged. Rufus launch skipped (--no-launch).", imagePath, RufusPath, arguments);
        }

        if (!File.Exists(RufusPath))
        {
            return new PiProvisionResult(false, $"Bundled Rufus writer not found at {RufusPath}.", imagePath, RufusPath, arguments);
        }

        _launcher.Launch(RufusPath, BuildLaunchCommandLine(imagePath, options.Profile));
        return new PiProvisionResult(true, "Image staged; RUFUS For MouseKeyProxy launched.", imagePath, RufusPath, arguments);
    }

    private void WriteManifest(PiProvisionOptions options, string imagePath)
    {
        var manifest = new
        {
            createdAtUtc = System.DateTimeOffset.UtcNow.ToString("O"),
            imageUrl = options.ImageUrl.ToString(),
            expectedSha256 = options.ExpectedSha256 ?? string.Empty,
            imagePath,
            rufusPath = RufusPath,
            profile = options.Profile,
            launchArguments = BuildLaunchArguments(imagePath, options.Profile)
        };

        var manifestPath = Path.Combine(options.StageRoot, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return System.Convert.ToHexString(hash).ToLowerInvariant();
    }
}
