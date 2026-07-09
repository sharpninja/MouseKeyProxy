using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[UnsetVisualStudioEnvironmentVariables]
class Build : NukeBuild
{
    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build")]
    readonly string Configuration = "Debug";

    [Parameter("NuGet API key. Defaults to NUGET_API_KEY from the environment.")]
    readonly string NuGetApiKey = Environment.GetEnvironmentVariable("NUGET_API_KEY") ?? string.Empty;

    /// <summary>
    /// Path to the rufus-mkp checkout. Defaults to RUFUS_MKP_ROOT env, then sibling
    /// <c>../rufus-mkp</c>, then <c>F:\GitHub\rufus-mkp</c> when present.
    /// </summary>
    [Parameter("Path to the rufus-mkp repository (MouseKeyProxy Rufus fork).")]
    readonly string RufusRoot = string.Empty;

    /// <summary>Named Pi HID profile for Rufus (--mkp-pi-profile). Default: default.</summary>
    [Parameter("Saved Rufus MouseKeyProxy Pi HID profile name.")]
    readonly string RufusProfile = "default";

    /// <summary>MSBuild/VS platform for Rufus (x64 or Win32). Default: x64.</summary>
    [Parameter("Rufus MSBuild platform (x64|Win32).")]
    readonly string RufusPlatform = "x64";

    /// <summary>When true, re-download/restage the Pi base image even if present.</summary>
    [Parameter("Force re-stage of the Pi OS image for CreatePiImage.")]
    readonly bool ForcePiImage = false;

    /// <summary>
    /// When true (default for BuildSdCard), run Rufus unattended: write image, eject, exit.
    /// Uses <c>--mkp-auto-write</c>. Pass <c>--AutoWrite false</c> for interactive GUI only.
    /// </summary>
    [Parameter("Unattended Rufus write + eject + exit after staging the Pi image.")]
    readonly bool AutoWrite = true;

    /// <summary>
    /// Optional PhysicalDrive number for unattended write (e.g. 2 for \\.\PhysicalDrive2).
    /// When empty, Rufus auto-picks only if exactly one removable drive is present.
    /// </summary>
    [Parameter("PhysicalDrive number for Rufus --device (unattended SD write).")]
    readonly string RufusDevice = string.Empty;

    string? _packageVersion;
    string? _assemblySemVer;
    string? _assemblySemFileVer;
    string? _informationalVersion;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath OutputDirectory => RootDirectory / "output";
    AbsolutePath SolutionPath => RootDirectory / "MouseKeyProxy.slnx";
    AbsolutePath PayloadsDirectory => OutputDirectory / "payloads";
    AbsolutePath ToolPackagesDirectory => OutputDirectory / "packages";
    AbsolutePath BundledRufusExe => RootDirectory / "assets" / "rufus" / "rufus.exe";
    AbsolutePath PiImageStageDirectory => OutputDirectory / "pi-image-stage";

    AbsolutePath ReplProject => SourceDirectory / "MouseKeyProxy.Repl" / "MouseKeyProxy.Repl.csproj";
    AbsolutePath ServiceProject => SourceDirectory / "MouseKeyProxy.Service" / "MouseKeyProxy.Service.csproj";
    AbsolutePath AgentProject => SourceDirectory / "MouseKeyProxy.Agent" / "MouseKeyProxy.Agent.csproj";
    AbsolutePath BootstrapProject => SourceDirectory / "MouseKeyProxy.Bootstrap" / "MouseKeyProxy.Bootstrap.csproj";

    string PackageVersion => _packageVersion ??= GetGitVersionVariable("SemVer");
    string AssemblySemVer => _assemblySemVer ??= GetGitVersionVariable("AssemblySemVer");
    string AssemblySemFileVer => _assemblySemFileVer ??= GetGitVersionVariable("AssemblySemFileVer");
    string InformationalVersion => _informationalVersion ??= GetGitVersionVariable("InformationalVersion");

    string VersionMsBuildProperties => string.Join(" ", new[]
    {
        MsBuildProperty("Version", PackageVersion),
        MsBuildProperty("PackageVersion", PackageVersion),
        MsBuildProperty("AssemblyVersion", AssemblySemVer),
        MsBuildProperty("FileVersion", AssemblySemFileVer)
    });

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            TestsDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(OutputDirectory);
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            RunDotNetCommand("tool restore");
            RunDotNetCommand($"restore {Quote(SolutionPath)}");
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            RunDotNetCommand($"build {Quote(SolutionPath)} -c {Configuration} --no-restore {VersionMsBuildProperties}");
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            // Default CI/dev run: everything except the two-machine lab E2E, which needs live lab
            // hardware. Those run via the opt-in IntegrationTest target.
            RunDotNetCommand($"test {Quote(SolutionPath)} -c {Configuration} --no-restore --no-build --filter \"Category!=TwoMachineE2E\" {VersionMsBuildProperties}");
        });

    Target IntegrationTest => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            // Opt-in lab run: only meaningful on a named lab peer with the service live on both ends.
            Environment.SetEnvironmentVariable("MKP_LAB_E2E", "1");
            RunDotNetCommand($"test {Quote(SolutionPath)} -c {Configuration} --no-restore --no-build --filter \"Category=TwoMachineE2E\" {VersionMsBuildProperties}");
        });

    // FR-MKP-011: parse the requirement docs + matrix and fail on malformed rows, orphan matrix
    // entries, or dangling trace links. Replaces the prior brittle Assert.Contains source scans.
    Target ValidateTraceability => _ => _
        .Executes(() =>
        {
            var projectDocs = RootDirectory / "docs" / "Project";
            var requirementDocs = new[]
            {
                projectDocs / "Functional-Requirements.md",
                projectDocs / "Technical-Requirements.md",
                projectDocs / "Testing-Requirements.md",
            };

            var idRegex = new Regex(@"\b(?:FR|TR|TEST)-[A-Z0-9]+(?:-[A-Z0-9]+)+\b");
            var defined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in requirementDocs)
            {
                if (!File.Exists(doc))
                {
                    throw new InvalidOperationException($"Missing requirement doc: {doc}");
                }

                foreach (Match m in idRegex.Matches(File.ReadAllText(doc)))
                {
                    defined.Add(m.Value);
                }
            }

            var matrixPath = projectDocs / "Requirements-Matrix.md";
            if (!File.Exists(matrixPath))
            {
                throw new InvalidOperationException($"Missing requirements matrix: {matrixPath}");
            }

            var errors = new List<string>();
            var matrixIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = File.ReadAllLines(matrixPath);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (!line.StartsWith("|"))
                {
                    continue;
                }

                var cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();

                // Skip the separator row (all dashes) and the header row.
                if (cells.All(c => c.Length == 0 || c.All(ch => ch == '-')))
                {
                    continue;
                }

                if (cells[0].Equals("Requirement", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (cells.Length != 5)
                {
                    errors.Add($"line {i + 1}: malformed matrix row (expected 5 columns, got {cells.Length}): {line}");
                    continue;
                }

                var requirementId = cells[0];
                matrixIds.Add(requirementId);
                if (!defined.Contains(requirementId))
                {
                    errors.Add($"line {i + 1}: matrix requirement '{requirementId}' is not defined in any requirement doc (orphan)");
                }

                foreach (Match m in idRegex.Matches(cells[4]))
                {
                    if (!defined.Contains(m.Value))
                    {
                        errors.Add($"line {i + 1}: trace link '{m.Value}' (for {requirementId}) is not defined in any requirement doc (dangling)");
                    }
                }
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Traceability validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
            }

            Console.WriteLine($"Traceability OK: {defined.Count} defined requirement IDs; {matrixIds.Count} matrix rows validated.");
        });

    // FR-MKP-011 / TEST-MKP-017 / TEST-MKP-018: run the Pester tests over the operator tooling scripts.
    Target TestPowerShell => _ => _
        .Executes(() =>
        {
            var script = TestsDirectory / "powershell" / "Run-PesterTests.ps1";
            RunProcess("pwsh", $"-NoProfile -NonInteractive -File {Quote(script)}", captureOutput: false);
        });

    Target ShowVersion => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            Console.WriteLine($"PackageVersion: {PackageVersion}");
            Console.WriteLine($"AssemblyVersion: {AssemblySemVer}");
            Console.WriteLine($"FileVersion: {AssemblySemFileVer}");
            Console.WriteLine($"InformationalVersion: {InformationalVersion}");
        });

    Target PackRepl => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            EnsureCleanDirectory(ToolPackagesDirectory);
            RunDotNetCommand($"pack {Quote(ReplProject)} -c {Configuration} -o {Quote(ToolPackagesDirectory)} --no-restore --no-build {VersionMsBuildProperties}");
            var package = GetToolPackagePath();
            Console.WriteLine($"Packed {package} with GitVersion package version {PackageVersion}.");
        });

    Target PublishService => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var outDir = PayloadsDirectory / "service";
            EnsureCleanDirectory(outDir);
            RunDotNetCommand($"publish {Quote(ServiceProject)} -c {Configuration} -o {Quote(outDir)} -r win-x64 --self-contained true -p:PublishSingleFile=true {VersionMsBuildProperties}");
        });

    Target PublishAgent => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var outDir = PayloadsDirectory / "agent";
            EnsureCleanDirectory(outDir);
            RunDotNetCommand($"publish {Quote(AgentProject)} -c {Configuration} -o {Quote(outDir)} -r win-x64 --self-contained true -p:PublishSingleFile=true {VersionMsBuildProperties}");
        });

    Target PublishSelfContained => _ => _
        .DependsOn(PublishService, PublishAgent)
        .Executes(() => { });

    /// <summary>
    /// FR-MKP-024: publish Bootstrap + stage a client install folder (payloads + Install.ps1 + bootstrap JSON).
    /// Produces <c>output/payloads/client-install/</c> suitable for MKP-DEPLOY/install/ or USB LUN.
    /// When WiX is available, also attempts MSI; otherwise the folder + Install.ps1 is the ship vehicle.
    /// </summary>
    Target PackClientMsi => _ => _
        .DependsOn(PublishService, PublishAgent)
        .Executes(() =>
        {
            var bootstrapOut = PayloadsDirectory / "bootstrap";
            EnsureCleanDirectory(bootstrapOut);
            RunDotNetCommand(
                $"publish {Quote(BootstrapProject)} -c {Configuration} -o {Quote(bootstrapOut)} -r win-x64 --self-contained true -p:PublishSingleFile=true {VersionMsBuildProperties}");

            var clientInstall = PayloadsDirectory / "client-install";
            EnsureCleanDirectory(clientInstall);
            Directory.CreateDirectory(clientInstall / "payloads" / "service");
            Directory.CreateDirectory(clientInstall / "payloads" / "agent");

            CopyDirectory(PayloadsDirectory / "service", clientInstall / "payloads" / "service");
            CopyDirectory(PayloadsDirectory / "agent", clientInstall / "payloads" / "agent");
            foreach (var f in Directory.GetFiles(bootstrapOut))
            {
                File.Copy(f, Path.Combine(clientInstall, Path.GetFileName(f)), overwrite: true);
            }

            var deviceUrl = Environment.GetEnvironmentVariable("MKP_DEVICE_GRPC")
                ?? "https://192.168.1.200:50051";
            var ticket = Environment.GetEnvironmentVariable("MKP_INSTALL_TICKET") ?? string.Empty;
            var bootstrap = new
            {
                schemaVersion = 1,
                devicePeerId = Environment.GetEnvironmentVariable("MKP_DEVICE_PEER_ID") ?? "mkp-hid-pi",
                deviceGrpcUrl = deviceUrl,
                discoveryPort = 50052,
                preferDiscovery = true,
                clientRole = "UsbConnectedPc",
                advertiseClientService = true,
                installTicket = string.IsNullOrWhiteSpace(ticket) ? null : ticket,
            };
            var json = System.Text.Json.JsonSerializer.Serialize(
                bootstrap,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            File.WriteAllText(clientInstall / "device-bootstrap.json", json);
            // Always materialize install-ticket.txt for WiX File include (even if empty ticket).
            var ticketText = string.IsNullOrWhiteSpace(ticket) ? "none" : ticket;
            File.WriteAllText(clientInstall / "install-ticket.txt", ticketText);

            var installPs1 = """
# FR-MKP-024: elevated client install from MKP-DEPLOY / USB LUN
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$boot = Join-Path $here 'MouseKeyProxy.Bootstrap.exe'
if (-not (Test-Path $boot)) { throw "Missing $boot" }
& $boot --payloads (Join-Path $here 'payloads') --bootstrap-json (Join-Path $here 'device-bootstrap.json')
exit $LASTEXITCODE
""";
            File.WriteAllText(clientInstall / "Install-MouseKeyProxy.ps1", installPs1);

            var autorun = """
[AutoRun]
label=MouseKeyProxy Client Install
open=Install-MouseKeyProxy.ps1
action=Install MouseKeyProxy Service + Agent
""";
            File.WriteAllText(clientInstall / "autorun.inf", autorun);

            var readme = """
MouseKeyProxy client install (FR-MKP-024/025/026)
1. Right-click Install-MouseKeyProxy.ps1 -> Run with PowerShell (Admin), or:
   pwsh -ExecutionPolicy Bypass -File .\Install-MouseKeyProxy.ps1
2. Installs Service + Agent, pairs to the Pi device, queues clipboard intro for the control host.
3. Keyboard/mouse from the control host go to the Pi HID only; clipboard attaches to this PC's Agent/Service.
""";
            File.WriteAllText(clientInstall / "README.txt", readme);

            // WiX v7 MSI (AcceptEula=wix7 in the .wixproj). Avoid trailing backslash in -p: values (escapes quotes).
            var wixProj = RootDirectory / "packaging" / "MouseKeyProxy.Client.Installer" / "MouseKeyProxy.Client.Installer.wixproj";
            var msiOutDir = PayloadsDirectory / "msi";
            EnsureCleanDirectory(msiOutDir);
            var clientInstallAbs = clientInstall.ToString().TrimEnd('\\', '/').Replace('\\', '/');
            var msiOutAbs = msiOutDir.ToString().TrimEnd('\\', '/').Replace('\\', '/') + "/";
            RunDotNetCommand(
                $"build {Quote(wixProj)} -c Release -p:ClientInstallDir={Quote(clientInstallAbs)} -p:OutputPath={Quote(msiOutAbs)}");

            var searchRoots = new[]
            {
                msiOutDir.ToString(),
                (RootDirectory / "packaging" / "MouseKeyProxy.Client.Installer" / "bin" / "Release").ToString(),
                (RootDirectory / "packaging" / "MouseKeyProxy.Client.Installer" / "bin" / Configuration).ToString(),
            };
            string? msi = null;
            foreach (var root in searchRoots.Where(Directory.Exists))
            {
                msi = Directory.GetFiles(root, "MouseKeyProxy-Client*.msi", SearchOption.AllDirectories).FirstOrDefault()
                    ?? Directory.GetFiles(root, "*.msi", SearchOption.AllDirectories).FirstOrDefault();
                if (msi is not null)
                {
                    break;
                }
            }

            if (msi is null)
            {
                throw new InvalidOperationException("WiX build completed but no MSI was found.");
            }

            var destMsi = Path.Combine(clientInstall, "MouseKeyProxy-Client.msi");
            if (!string.Equals(Path.GetFullPath(msi), Path.GetFullPath(destMsi), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(msi, destMsi, overwrite: true);
            }

            var msiCanonical = Path.Combine(msiOutDir, "MouseKeyProxy-Client.msi");
            if (!string.Equals(Path.GetFullPath(msi), Path.GetFullPath(msiCanonical), StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Copy(msi, msiCanonical, overwrite: true);
                }
                catch (IOException ex)
                {
                    Console.WriteLine("MSI copy to payloads/msi skipped (in use): " + ex.Message);
                }
            }

            Console.WriteLine("MSI: " + destMsi + " (" + new FileInfo(destMsi).Length + " bytes)");
            File.WriteAllText(clientInstall / "autorun.inf",
                "[AutoRun]\r\nlabel=MouseKeyProxy Client Install\r\nopen=MouseKeyProxy-Client.msi\r\naction=Install MouseKeyProxy\r\n");

            Console.WriteLine("Client install staged: " + clientInstall.ToString());
        });

    /// <summary>
    /// FR-MKP-024: copy client-install tree into pi-stage/install for MKP-DEPLOY,
    /// and stage the client MSI into pi-stage/share for the USB thumb LUN
    /// (folder → thumb.img via setup-configfs-gadget.sh).
    /// </summary>
    Target StagePiInstallMedia => _ => _
        .DependsOn(PackClientMsi)
        .Executes(() =>
        {
            var src = PayloadsDirectory / "client-install";
            var destInstall = PiStageDirectory / "install";
            EnsureCleanDirectory(destInstall);
            CopyDirectory(src, destInstall);
            Console.WriteLine($"Staged Pi install media under {destInstall}");

            // Thumb-drive seed: single MSI (+ autorun) under share/ → rootfs
            // /mnt/mkp-deploy/share → VFAT thumb.img (one host LUN).
            var destShare = PiStageDirectory / "share";
            EnsureCleanDirectory(destShare);
            var msiSrc = src / "MouseKeyProxy-Client.msi";
            if (!File.Exists(msiSrc))
            {
                throw new FileNotFoundException(
                    "MouseKeyProxy-Client.msi missing after PackClientMsi.", msiSrc);
            }

            File.Copy(msiSrc, destShare / "MouseKeyProxy-Client.msi", overwrite: true);
            File.WriteAllText(destShare / "autorun.inf",
                "[AutoRun]\r\nlabel=MouseKeyProxy Client\r\nopen=MouseKeyProxy-Client.msi\r\naction=Install MouseKeyProxy\r\n");
            File.WriteAllText(destShare / "README.txt",
                "MouseKeyProxy client MSI (USB thumb LUN)\r\n" +
                "Double-click MouseKeyProxy-Client.msi to install Service + Agent on this PC.\r\n");
            var msiLen = new FileInfo(destShare / "MouseKeyProxy-Client.msi").Length;
            Console.WriteLine($"Staged thumb share under {destShare} (MSI {msiLen} bytes)");
        });

    static void CopyDirectory(AbsolutePath source, AbsolutePath destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, destination));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, destination);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    AbsolutePath PiStageDirectory => OutputDirectory / "pi-stage";

    // FR-MKP-012: publish the Service + Repl for the Pi as ready-to-run, single-file, self-contained
    // linux-arm64 - the artifacts rufus stages recursively into the rootfs. Trimming is intentionally
    // NOT enabled: ASP.NET Core + gRPC is not trim-safe and would break at runtime on the Pi.
    Target PublishPi => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var flags = "-r linux-arm64 --self-contained true "
                + "-p:PublishSingleFile=true -p:PublishReadyToRun=true "
                + "-p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true";

            var serviceOut = PiStageDirectory / "service";
            EnsureCleanDirectory(serviceOut);
            RunDotNetCommand($"publish {Quote(ServiceProject)} -c {Configuration} -o {Quote(serviceOut)} {flags} {VersionMsBuildProperties}");

            var replOut = PiStageDirectory / "repl";
            EnsureCleanDirectory(replOut);
            RunDotNetCommand($"publish {Quote(ReplProject)} -c {Configuration} -o {Quote(replOut)} {flags} {VersionMsBuildProperties}");

            Console.WriteLine($"Pi publish staged under {PiStageDirectory}.");
        });

    Target PublishToolToNuGet => _ => _
        .DependsOn(PackRepl)
        .Executes(() =>
        {
            if (string.IsNullOrWhiteSpace(NuGetApiKey))
            {
                throw new InvalidOperationException("NUGET_API_KEY is required to publish MouseKeyProxy.Repl.");
            }

            EnsureHeadIsLatestTaggedCommit();
            var package = GetToolPackagePath();
            RunDotNetCommand($"nuget push {Quote(package)} --api-key {Quote(NuGetApiKey)} --source https://api.nuget.org/v3/index.json --skip-duplicate");
        });

    Target FullBuild => _ => _
        .DependsOn(Clean, Restore, Compile, Test, PackRepl, PublishService, PublishAgent)
        .Executes(() =>
        {
            Console.WriteLine($"Full build completed with GitVersion package version {PackageVersion}.");
        });

    // -------------------------------------------------------------------------
    // Rufus (rufus-mkp fork) operator targets
    // -------------------------------------------------------------------------

    /// <summary>
    /// Build rufus-mkp (MinGW <c>make</c> preferred; falls back to MSBuild on <c>rufus.sln</c>)
    /// and copy <c>rufus.exe</c> into <c>assets/rufus/rufus.exe</c> for the mkp tool bundle.
    /// </summary>
    Target BuildRufus => _ => _
        .Executes(() =>
        {
            var root = ResolveRufusRoot();
            Console.WriteLine($"Building Rufus from {root}");

            var built = TryBuildRufusWithMake(root) ?? TryBuildRufusWithMsBuild(root);
            if (built is null || !File.Exists(built))
            {
                throw new InvalidOperationException(
                    "Could not build rufus.exe. Install MinGW (make) or Visual Studio MSBuild, " +
                    $"and ensure rufus-mkp is at {root}.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(BundledRufusExe)!);
            File.Copy(built, BundledRufusExe, overwrite: true);
            Console.WriteLine($"Built Rufus: {built}");
            Console.WriteLine($"Copied to bundle: {BundledRufusExe} ({new FileInfo(BundledRufusExe).Length} bytes)");
        });

    /// <summary>
    /// Launch Rufus interactively (GUI). Rebuilds rufus-mkp first so dialog/resource
    /// changes are visible. Optional: <c>--RufusProfile default</c> loads a saved profile.
    /// Does not wait for the GUI process to exit.
    /// </summary>
    Target LaunchRufus => _ => _
        .DependsOn(BuildRufus)
        .Executes(() =>
        {
            var exe = ResolveRufusExe(requireExists: true);
            var args = new List<string> { "--gui" };
            if (!string.IsNullOrWhiteSpace(RufusProfile))
            {
                args.Add($"--mkp-pi-profile={NormalizeProfileName(RufusProfile)}");
            }

            var argLine = string.Join(" ", args.Select(a => a.Contains(' ') ? Quote(a) : a));
            Console.WriteLine($"Launching Rufus: {exe} {argLine}");
            LaunchInteractive(exe, argLine);
        });

    /// <summary>
    /// Stage a Raspberry Pi OS Lite arm64 image and launch Rufus with a saved profile.
    /// With <c>--AutoWrite true</c> (default), runs unattended write + eject + exit via
    /// <c>--mkp-auto-write</c> and waits for completion. With <c>--AutoWrite false</c>,
    /// opens the GUI only (operator completes Start).
    /// Rebuilds Rufus first so first-boot/UI changes are included.
    /// Always depends on PublishPi + StagePiInstallMedia so MKP_PI_STAGE_DIR has
    /// Service/Repl and the MSI thumb seed before Rufus writes.
    /// </summary>
    Target CreatePiImage => _ => _
        .DependsOn(BuildRufus, PublishPi, StagePiInstallMedia)
        .Executes(() =>
        {
            var root = ResolveRufusRoot();
            var exe = ResolveRufusExe(requireExists: true);
            var profile = NormalizeProfileName(RufusProfile);
            var stageScript = root / "scripts" / "stage-mkp-pi-image.ps1";
            string imagePath;

            if (File.Exists(stageScript))
            {
                var forceFlag = ForcePiImage ? " -Force" : string.Empty;
                // Always stage first; AutoWrite path launches Rufus itself so it can wait.
                var args =
                    $"-NoProfile -ExecutionPolicy Bypass -File {Quote(stageScript)} " +
                    $"-StageRoot {Quote(PiImageStageDirectory)} " +
                    $"-RufusPath {Quote(exe)} " +
                    $"-Profile {Quote(profile)}" +
                    forceFlag;
                if (!AutoWrite)
                {
                    args += " -LaunchRufus";
                }

                Console.WriteLine($"Staging Pi image via {stageScript}");
                Console.WriteLine($"  profile={profile} AutoWrite={AutoWrite}");
                RunProcess("pwsh", args, captureOutput: false);
                imagePath = FindOrRequireStagedPiImage(PiImageStageDirectory);
                if (!AutoWrite)
                {
                    Console.WriteLine("Rufus GUI launched (interactive). Complete Start, then eject manually.");
                    return;
                }
            }
            else
            {
                Directory.CreateDirectory(PiImageStageDirectory);
                imagePath = FindOrRequireStagedPiImage(PiImageStageDirectory);
                if (!AutoWrite)
                {
                    var cmd = $"--gui --iso {Quote(imagePath)} --mkp-pi-profile={profile}";
                    Console.WriteLine($"Stage script missing; launching Rufus interactively:");
                    Console.WriteLine($"  {exe} {cmd}");
                    LaunchInteractive(exe, cmd);
                    return;
                }
            }

            RunRufusAutoWrite(exe, imagePath, profile);
        });

    /// <summary>Alias for <see cref="CreatePiImage"/> (saved Rufus config → new appliance image write).</summary>
    Target CreateImageFromRufusConfig => _ => _
        .DependsOn(CreatePiImage)
        .Executes(() => { });

    /// <summary>
    /// Full SD-card build path: linux-arm64 Service/Repl (<see cref="PublishPi"/>),
    /// client MSI + install kit staged for MKP-DEPLOY (<see cref="StagePiInstallMedia"/>),
    /// then unattended Rufus image write + eject (<see cref="CreatePiImage"/> / AutoWrite).
    /// </summary>
    /// <remarks>
    /// Optional env for the client kit: <c>MKP_INSTALL_TICKET</c>, <c>MKP_DEVICE_GRPC</c>,
    /// <c>MKP_DEVICE_PEER_ID</c>. Optional flags: <c>--RufusProfile default</c>,
    /// <c>--ForcePiImage</c>, <c>--AutoWrite true|false</c>, <c>--RufusDevice N</c>.
    /// After the write, copy <c>output/pi-stage/install/*</c> onto
    /// <c>MKP-DEPLOY/install/</c> if the Rufus profile does not embed that tree.
    /// </remarks>
    Target BuildSdCard => _ => _
        .DependsOn(PublishPi, StagePiInstallMedia, CreatePiImage)
        .Executes(() =>
        {
            Console.WriteLine("BuildSdCard complete.");
            Console.WriteLine($"  Pi Service/Repl: {PiStageDirectory}");
            Console.WriteLine($"  Client install kit: {PiStageDirectory / "install"}");
            Console.WriteLine($"  Client MSI (also under payloads): {PayloadsDirectory / "client-install" / "MouseKeyProxy-Client.msi"}");
            if (AutoWrite)
            {
                Console.WriteLine("  Rufus unattended write finished (card should be ejected).");
            }
            else
            {
                Console.WriteLine("  Rufus GUI was launched; complete write in the GUI, then eject.");
            }

            Console.WriteLine("  If MKP-DEPLOY/install is empty on first boot, copy output/pi-stage/install/* onto the deploy partition.");
        });

    AbsolutePath ResolveRufusRoot()
    {
        if (!string.IsNullOrWhiteSpace(RufusRoot) && Directory.Exists(RufusRoot))
        {
            return (AbsolutePath)Path.GetFullPath(RufusRoot);
        }

        var fromEnv = Environment.GetEnvironmentVariable("RUFUS_MKP_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
        {
            return (AbsolutePath)Path.GetFullPath(fromEnv);
        }

        var sibling = Path.GetFullPath(Path.Combine(RootDirectory, "..", "rufus-mkp"));
        if (Directory.Exists(sibling))
        {
            return (AbsolutePath)sibling;
        }

        const string labDefault = @"F:\GitHub\rufus-mkp";
        if (Directory.Exists(labDefault))
        {
            return (AbsolutePath)labDefault;
        }

        throw new InvalidOperationException(
            "rufus-mkp root not found. Pass --RufusRoot <path> or set RUFUS_MKP_ROOT.");
    }

    AbsolutePath ResolveRufusExe(bool requireExists)
    {
        try
        {
            var root = ResolveRufusRoot();
            foreach (var candidate in EnumerateRufusBuildOutputs(root))
            {
                if (File.Exists(candidate))
                {
                    return (AbsolutePath)candidate;
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Fork optional when only the bundled exe is needed.
        }

        if (File.Exists(BundledRufusExe))
        {
            return BundledRufusExe;
        }

        if (requireExists)
        {
            throw new FileNotFoundException(
                "rufus.exe not found. Run target BuildRufus first, or place a binary at assets/rufus/rufus.exe.");
        }

        return BundledRufusExe;
    }

    static IEnumerable<string> EnumerateRufusBuildOutputs(AbsolutePath root)
    {
        yield return root / "src" / "rufus.exe";
        yield return root / "x64" / "Release" / "rufus.exe";
        yield return root / "x64" / "Debug" / "rufus.exe";
        yield return root / "x86" / "Release" / "rufus.exe";
        yield return root / "x86" / "Debug" / "rufus.exe";
    }

    string? TryBuildRufusWithMake(AbsolutePath root)
    {
        EnsureMingwOnPath();
        var make = FindOnPath("make.exe") ?? FindOnPath("mingw32-make.exe")
            ?? (File.Exists(@"C:\msys64\usr\bin\make.exe") ? @"C:\msys64\usr\bin\make.exe" : null);
        if (make is null)
        {
            Console.WriteLine("make not on PATH; skipping MinGW Rufus build.");
            return null;
        }

        try
        {
            Console.WriteLine($"Building Rufus with: {make}");
            RunProcessInDirectory(make, "-j", root, captureOutput: false);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"MinGW make build failed: {ex.Message}");
            return null;
        }

        var exe = root / "src" / "rufus.exe";
        return File.Exists(exe) ? exe : null;
    }

    /// <summary>
    /// Prepend common MSYS2 MinGW paths so <c>make</c>/<c>gcc</c>/<c>windres</c> resolve
    /// when the operator shell does not include them.
    /// </summary>
    static void EnsureMingwOnPath()
    {
        var extras = new[]
        {
            @"C:\msys64\mingw64\bin",
            @"C:\msys64\usr\bin",
            @"C:\msys64\mingw32\bin",
        };
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var parts = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        foreach (var dir in extras.Reverse())
        {
            if (Directory.Exists(dir) &&
                !parts.Any(p => string.Equals(p, dir, StringComparison.OrdinalIgnoreCase)))
            {
                parts.Insert(0, dir);
            }
        }

        Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, parts));
    }

    string? TryBuildRufusWithMsBuild(AbsolutePath root)
    {
        var sln = root / "rufus.sln";
        if (!File.Exists(sln))
        {
            Console.WriteLine($"No rufus.sln at {sln}");
            return null;
        }

        var msbuild = FindMsBuild();
        if (msbuild is null)
        {
            Console.WriteLine("MSBuild not found; skipping VS Rufus build.");
            return null;
        }

        var platform = string.IsNullOrWhiteSpace(RufusPlatform) ? "x64" : RufusPlatform;
        if (platform.Equals("x86", StringComparison.OrdinalIgnoreCase))
        {
            platform = "Win32";
        }

        var config = Configuration.Equals("Debug", StringComparison.OrdinalIgnoreCase) ? "Debug" : "Release";
        var args = $"{Quote(sln)} /m /p:Configuration={config} /p:Platform={platform} /v:m";
        try
        {
            RunProcessInDirectory(msbuild, args, root, captureOutput: false);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"MSBuild Rufus build failed: {ex.Message}");
            return null;
        }

        foreach (var candidate in EnumerateRufusBuildOutputs(root))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    static string? FindMsBuild()
    {
        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (File.Exists(vswhere))
        {
            try
            {
                var psi = new ProcessStartInfo(vswhere,
                    "-latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p is not null)
                {
                    var line = p.StandardOutput.ReadLine();
                    p.WaitForExit(15000);
                    if (!string.IsNullOrWhiteSpace(line) && File.Exists(line.Trim()))
                    {
                        return line.Trim();
                    }
                }
            }
            catch
            {
                // fall through
            }
        }

        return FindOnPath("MSBuild.exe");
    }

    static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // ignore bad PATH entries
            }
        }

        return null;
    }

    static string NormalizeProfileName(string profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return "default";
        }

        var chars = profile.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (!(char.IsLetterOrDigit(c) || c is '-' or '_' or '.'))
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    static string FindOrRequireStagedPiImage(AbsolutePath stageDir)
    {
        var preferred = new[]
        {
            stageDir / "raspios-lite-arm64.img",
            stageDir / "raspios-lite-arm64.img.xz",
        };
        foreach (var p in preferred)
        {
            if (File.Exists(p))
            {
                return p;
            }
        }

        var any = Directory.Exists(stageDir)
            ? Directory.EnumerateFiles(stageDir, "*.img*", SearchOption.TopDirectoryOnly).FirstOrDefault()
            : null;
        if (any is not null)
        {
            return any;
        }

        throw new FileNotFoundException(
            $"No Pi image under {stageDir}. Clone rufus-mkp (with scripts/stage-mkp-pi-image.ps1) " +
            "or place a .img/.img.xz in that directory, then re-run CreatePiImage.");
    }

    void LaunchInteractive(string exePath, string arguments)
    {
        var psi = new ProcessStartInfo(exePath, arguments)
        {
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? RootDirectory,
            UseShellExecute = true,
        };
        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {exePath}.");
        Console.WriteLine($"Rufus started (PID {process.Id}). This target does not wait for the GUI to close.");
    }

    /// <summary>
    /// Launch Rufus with <c>--mkp-auto-write</c>, wait for exit, fail the build on non-zero.
    /// Requires elevation (UAC) for disk write; uses ShellExecute so the manifest self-elevates.
    /// </summary>
    void RunRufusAutoWrite(string exePath, string imagePath, string profile)
    {
        // --gui disables the console hogger; window stays hidden under --mkp-auto-write.
        var args = new List<string>
        {
            "--gui",
            "--iso",
            imagePath,
            $"--mkp-pi-profile={profile}",
            "--mkp-auto-write",
        };

        var device = ResolveRufusDeviceNumber();
        if (device is not null)
        {
            args.Add("--device");
            args.Add(device.Value.ToString());
        }

        var argLine = string.Join(" ", args.Select(a => a.Contains(' ') ? Quote(a) : a));
        Console.WriteLine($"Unattended Rufus write: {exePath}");
        Console.WriteLine($"  {argLine}");
        Console.WriteLine("  Waiting for write + eject to finish (UAC may prompt)...");

        var stageDir = (RootDirectory / "output" / "pi-stage").ToString();
        if (Directory.Exists(stageDir))
        {
            Environment.SetEnvironmentVariable("MKP_PI_STAGE_DIR", stageDir);
            Console.WriteLine($"  MKP_PI_STAGE_DIR={stageDir}");
        }
        else
        {
            Console.WriteLine($"  MKP_PI_STAGE_DIR not set (missing {stageDir}); rootfs service staging will be skipped.");
        }

        // Drop Windows mount points / automount so the image write does not hit
        // "Access is denied" mid-stream when Explorer remounts the card.
        if (device is not null)
        {
            PrepareDiskForUnattendedWrite(device.Value);
        }

        var psi = new ProcessStartInfo(exePath, argLine)
        {
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? RootDirectory,
            // ShellExecute so the rufus.exe elevation manifest can self-elevate.
            // Window is hidden by --mkp-auto-write inside rufus-mkp.
            UseShellExecute = true,
        };

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start {exePath}.");
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                var logHint = Path.Combine(
                    Path.GetDirectoryName(exePath) ?? string.Empty,
                    "rufus-mkp-autowrite.log");
                if (!File.Exists(logHint))
                {
                    logHint = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "rufus", "rufus-mkp-autowrite.log");
                }

                throw new InvalidOperationException(
                    $"Rufus auto-write failed with exit code {process.ExitCode}. " +
                    $"See {logHint}; use --RufusDevice N if multiple drives are present. " +
                    "Exit codes: 0=ok, 1=write failed, 2=need --device, 3=no device, 4=image scan failed, 5=cancelled.");
            }

            Console.WriteLine("Rufus auto-write succeeded (exit 0); media should be ejected.");
        }
        finally
        {
            // Re-enable automount for subsequent media insertion.
            try
            {
                RunCapture("mountvol", "/E");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: could not re-enable automount: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Best-effort: disable automount and remove access paths from the target disk so
    /// Rufus can open exclusive write without mid-stream Access Denied from remounts.
    /// </summary>
    void PrepareDiskForUnattendedWrite(int physicalDriveNumber)
    {
        Console.WriteLine($"Preparing PhysicalDrive{physicalDriveNumber} for unattended write (dismount + no automount)...");
        try
        {
            RunCapture("mountvol", "/N");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  mountvol /N failed (continuing): {ex.Message}");
        }

        var script =
            "$ErrorActionPreference='Continue'; " +
            $"$parts = Get-Partition -DiskNumber {physicalDriveNumber} -ErrorAction SilentlyContinue; " +
            "foreach ($p in $parts) { " +
            "  foreach ($path in @($p.AccessPaths)) { " +
            "    if ($path -match '^[A-Z]:\\\\$') { " +
            "      try { Remove-PartitionAccessPath -DiskNumber $p.DiskNumber -PartitionNumber $p.PartitionNumber -AccessPath $path -ErrorAction Stop; " +
            "           Write-Output \"removed $path\" } catch { Write-Output \"skip $path : $($_.Exception.Message)\" } " +
            "    } " +
            "  } " +
            "}";
        try
        {
            var output = RunCapture("pwsh", $"-NoProfile -Command {Quote(script)}");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Console.WriteLine($"  {output.Replace("\r\n", " | ")}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Partition access-path removal failed (continuing): {ex.Message}");
        }
    }

    /// <summary>
    /// Resolve PhysicalDrive number from <see cref="RufusDevice"/>, or auto-detect a single
    /// removable/SD disk when the parameter is empty.
    /// </summary>
    int? ResolveRufusDeviceNumber()
    {
        if (!string.IsNullOrWhiteSpace(RufusDevice))
        {
            if (!int.TryParse(RufusDevice.Trim(), out var parsed) || parsed < 0)
            {
                throw new InvalidOperationException(
                    $"--RufusDevice must be a non-negative PhysicalDrive number; got '{RufusDevice}'.");
            }

            Console.WriteLine($"Using --RufusDevice={parsed} (PhysicalDrive{parsed}).");
            return parsed;
        }

        try
        {
            // BusType only: never match FriendlyName with bare "SD" (matches "SSD").
            var script =
                "Get-Disk | Where-Object { " +
                "$_.BusType -in @('USB','SD','MMC') -and -not $_.IsSystem -and -not $_.IsBoot -and $_.Size -gt 0 " +
                "} | Select-Object -ExpandProperty Number";
            var output = RunCapture("pwsh", $"-NoProfile -Command {Quote(script)}");
            var numbers = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => int.TryParse(s, out _))
                .Select(int.Parse)
                .Distinct()
                .ToList();

            if (numbers.Count == 1)
            {
                Console.WriteLine($"Auto-detected single removable/SD disk: PhysicalDrive{numbers[0]}.");
                return numbers[0];
            }

            if (numbers.Count > 1)
            {
                Console.WriteLine(
                    $"Multiple removable/SD disks found ({string.Join(", ", numbers)}). " +
                    "Pass --RufusDevice N, or leave empty only when Rufus lists exactly one target.");
            }
            else
            {
                Console.WriteLine("No USB/SD disk auto-detected; Rufus will require exactly one listed device.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Removable disk auto-detect skipped: {ex.Message}");
        }

        return null;
    }

    void RunProcessInDirectory(string fileName, string arguments, AbsolutePath workingDirectory, bool captureOutput)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        string stdout = string.Empty;
        string stderr = string.Empty;
        if (captureOutput)
        {
            stdout = process.StandardOutput.ReadToEnd();
            stderr = process.StandardError.ReadToEnd();
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var details = captureOutput ? Environment.NewLine + stdout + stderr : string.Empty;
            throw new InvalidOperationException($"{fileName} {arguments} failed with exit code {process.ExitCode}.{details}");
        }
    }

    string GetGitVersionVariable(string variable)
    {
        var value = RunCapture("dotnet", $"tool run dotnet-gitversion /showvariable {variable}");
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"GitVersion did not return {variable}.");
        }

        return value.Trim();
    }

    AbsolutePath GetToolPackagePath()
    {
        var expected = ToolPackagesDirectory / $"MouseKeyProxy.Repl.{PackageVersion}.nupkg";
        if (File.Exists(expected))
        {
            return expected;
        }

        var packages = ToolPackagesDirectory.GlobFiles("MouseKeyProxy.Repl.*.nupkg")
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .ToList();
        if (packages.Count == 0)
        {
            throw new FileNotFoundException($"No MouseKeyProxy.Repl package was found in {ToolPackagesDirectory}.");
        }

        return packages[0];
    }

    void EnsureHeadIsLatestTaggedCommit()
    {
        RunCapture("git", "fetch --tags origin");
        var head = RunCapture("git", "rev-parse HEAD");
        var latestTaggedCommit = RunCapture("git", "rev-list --tags --max-count=1");
        if (string.IsNullOrWhiteSpace(latestTaggedCommit))
        {
            throw new InvalidOperationException("Cannot publish: no Git tags exist.");
        }

        if (!string.Equals(head, latestTaggedCommit, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Cannot publish: HEAD {head} is not the latest tagged commit {latestTaggedCommit}.");
        }

        var exactTag = RunCapture("git", "describe --tags --exact-match HEAD");
        if (string.IsNullOrWhiteSpace(exactTag))
        {
            throw new InvalidOperationException("Cannot publish: HEAD is not exactly tagged.");
        }

        Console.WriteLine($"Publishing from latest tagged commit {head} ({exactTag}).");
    }

    void RunDotNetCommand(string arguments)
    {
        RunProcess("dotnet", arguments, captureOutput: false);
    }

    string RunCapture(string fileName, string arguments)
    {
        return RunProcess(fileName, arguments, captureOutput: true).Trim();
    }

    string RunProcess(string fileName, string arguments, bool captureOutput)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = RootDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = captureOutput,
            RedirectStandardError = captureOutput
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
        string stdout = string.Empty;
        string stderr = string.Empty;
        if (captureOutput)
        {
            stdout = process.StandardOutput.ReadToEnd();
            stderr = process.StandardError.ReadToEnd();
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            var details = captureOutput ? Environment.NewLine + stdout + stderr : string.Empty;
            throw new InvalidOperationException($"{fileName} {arguments} failed with exit code {process.ExitCode}.{details}");
        }

        return stdout;
    }

    static void DeleteDirectory(AbsolutePath path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    static void EnsureCleanDirectory(AbsolutePath path)
    {
        DeleteDirectory(path);
        Directory.CreateDirectory(path);
    }
    static string MsBuildProperty(string name, string value) => $"/p:{name}={Quote(value)}";

    static string Quote(object value) => $"\"{value.ToString()?.Replace("\"", "\\\"")}\"";
}
