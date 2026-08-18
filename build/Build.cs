using System;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[GitHubActions(
    "Build & Test",
    GitHubActionsImage.UbuntuLatest,
    OnPushBranches = ["main"],
    OnPullRequestBranches = ["main"],
    InvokedTargets = [nameof(Test), nameof(VerifyGenerated), nameof(AotSmoke)])]
[TrustedPublishingGitHubActions(
    "Manual Nuget Push",
    GitHubActionsImage.UbuntuLatest,
    On = [GitHubActionsTrigger.WorkflowDispatch],
    InvokedTargets = [nameof(NugetPush)],
    NugetUser = "${{ secrets.NUGET_USER }}")]
class Build : NukeBuild
{
    public static int Main () => Execute<Build>(x => x.Compile);

    [Solution] readonly Solution Solution;

    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            ArtifactsDirectory.CreateOrCleanDirectory();
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
                .SetConfiguration("Release")
                .EnableNoRestore());
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            // Release, matching Compile: reuses its output instead of a second Debug build, and
            // tests the configuration that ships.
            DotNetTest(s => s
                .AddProcessAdditionalArguments("--solution", Solution)
                .AddProcessAdditionalArguments("--configuration", "Release"));
        });

    Target VerifyGenerated => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            // Fails when src/*/Generated drifts from the checked-in schema snapshot.
            DotNetRun(_ => _
                .SetProjectFile(Solution.AllProjects.Single(x => x.Name == "Ocsf.Generator"))
                .SetConfiguration("Release")
                .SetApplicationArguments("verify"));
        });

    Target AotSmoke => _ => _
        .Executes(() =>
        {
            var project = Solution.AllProjects.Single(x => x.Name == "Ocsf.AotSmoke");
            var output = ArtifactsDirectory / "aot-smoke";
            DotNetPublish(_ => _
                .SetProject(project)
                .SetConfiguration("Release")
                .SetOutput(output));

            var exe = output / (OperatingSystem.IsWindows() ? "Ocsf.AotSmoke.exe" : "Ocsf.AotSmoke");
            ProcessTasks.StartProcess(exe, workingDirectory: output).AssertZeroExitCode();
        });

    static readonly string[] PackableProjects =
    [
        "Ocsf",
        "Ocsf.Validation",
    ];

    Target NugetPack => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            foreach (var name in PackableProjects)
            {
                var project = Solution.AllProjects.Single(x => x.Name == name);
                DotNetPack(_ => _
                    .SetProject(project)
                    .SetConfiguration("Release")
                    .EnableContinuousIntegrationBuild()
                    .SetOutputDirectory(ArtifactsDirectory));
            }
        });

    [Parameter("NuGet API key, short-lived key issued by NuGet/login via trusted publishing")] [Secret] readonly string NugetApiKey;

    Target NugetPush => _ => _
        .DependsOn(NugetPack)
        .Requires(() => !string.IsNullOrEmpty(NugetApiKey))
        .Executes(() =>
        {
            DotNetNuGetPush(_ => _
                .SetSource("https://api.nuget.org/v3/index.json")
                .SetTargetPath(ArtifactsDirectory / "*.nupkg")
                .EnableSkipDuplicate()
                .EnableNoSymbols()
                .SetApiKey(NugetApiKey));
        });

}
