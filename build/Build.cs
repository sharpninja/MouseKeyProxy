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

    AbsolutePath ReplProject => SourceDirectory / "MouseKeyProxy.Repl" / "MouseKeyProxy.Repl.csproj";
    AbsolutePath ServiceProject => SourceDirectory / "MouseKeyProxy.Service" / "MouseKeyProxy.Service.csproj";
    AbsolutePath AgentProject => SourceDirectory / "MouseKeyProxy.Agent" / "MouseKeyProxy.Agent.csproj";

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
