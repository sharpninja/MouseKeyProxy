using System;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[UnsetVisualStudioEnvironmentVariables]
class Build : NukeBuild
{
    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build")]
    readonly string Configuration = "Debug";

    [Solution] readonly Solution Solution;

    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath TestsDirectory => RootDirectory / "tests";
    AbsolutePath OutputDirectory => RootDirectory / "output";

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
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoRestore());
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetTest(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoRestore()
                .EnableNoBuild());
        });

    Target PackRepl => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetPack(s => s
                .SetProject(Solution.GetProject("MouseKeyProxy.Repl"))
                .SetConfiguration(Configuration)
                .SetOutputDirectory(OutputDirectory)
                .EnableNoRestore()
                .EnableNoBuild());
        });

    AbsolutePath PayloadsDirectory => OutputDirectory / "payloads";

    Target PublishService => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var outDir = PayloadsDirectory / "service";
            EnsureCleanDirectory(outDir);
            DotNetPublish(s => s
                .SetProject(Solution.GetProject("MouseKeyProxy.Service"))
                .SetConfiguration(Configuration)
                .SetRuntime("win-x64")
                .EnableSelfContained()
                .SetPublishSingleFile(true)
                .SetOutput(outDir));
        });

    Target PublishAgent => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var outDir = PayloadsDirectory / "agent";
            EnsureCleanDirectory(outDir);
            DotNetPublish(s => s
                .SetProject(Solution.GetProject("MouseKeyProxy.Agent"))
                .SetConfiguration(Configuration)
                .SetRuntime("win-x64")
                .EnableSelfContained()
                .SetPublishSingleFile(true)
                .SetOutput(outDir));
        });

    Target PublishSelfContained => _ => _
        .DependsOn(PublishService, PublishAgent)
        .Executes(() => { });

    Target FullBuild => _ => _
        .DependsOn(Clean, Restore, Compile, Test, PackRepl, PublishService, PublishAgent)
        .Executes(() =>
        {
            // Full build note logged via console in practice
        });
}
