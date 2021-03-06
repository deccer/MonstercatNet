#tool nuget:?package=NUnit.ConsoleRunner&version=3.11.1
#tool nuget:?package=ReportGenerator&version=4.6.7
#tool nuget:?package=Codecov&version=1.12.3
#tool nuget:?package=GitVersion.CommandLine&version=5.3.7
#tool nuget:?package=Microsoft.CodeCoverage&version=16.7.1

#addin nuget:?package=Cake.Codecov&version=0.9.1
#addin nuget:?package=Cake.Incubator&version=5.1.0
#addin nuget:?package=Cake.GitVersioning&version=3.2.31

using Cake.Core;

///////////////////////////////////////////////////////////////////////////////
// SETUP
///////////////////////////////////////////////////////////////////////////////

var Configuration = Argument("configuration", "Release");

const string Platform = "AnyCPU";
const string SolutionPath ="./MonstercatNet.sln";
const string AssemblyInfoPath ="./SharedAssemblyInfo.cs";
const string PackagePath = "./packages";
const string ResultsPath = "./results";
const string CoberturaResultsPath = "results/reports/cobertura";
const string localNugetDirectory = @"D:\Drop\NuGet";

var reportsFolder = new DirectoryPath(ResultsPath).Combine("reports");
var coberturaResultFile = Context.Environment.WorkingDirectory.Combine(CoberturaResultsPath).CombineWithFilePath("Cobertura.xml");
var vstestResultsFile = new FilePath("vsTestResults.trx");
var codeCoverageBinaryFile = new FilePath("vsCodeCoverage.coverage");
var codeCoverageResultsFile = new FilePath("vsCodeCoverage.xml");

var publicRelease = false;

// projects that are supposed to generate a nuget package
var nugetPackageProjects = new[]
{
    @".\MonstercatNet\MonstercatNet.csproj",
};

var ReportGeneratorSettings = new ReportGeneratorSettings()
{
    AssemblyFilters = new[]
    {
        "-MonstercatNet.Tests*",
        "-nunit3*",
        "-refit*"
    },
    ClassFilters = new[]
    {
        "-System*",
        "-Microsoft*",
    }
};

private void GenerateReport(FilePath inputFile, ReportGeneratorReportType type, string subFolder)
{
    ReportGeneratorSettings.ReportTypes = new[]
    {
        type
    };

    ReportGenerator(inputFile, reportsFolder.Combine(subFolder), ReportGeneratorSettings);
}

private void MergeReports(string pattern, ReportGeneratorReportType type, string subFolder)
{
    ReportGeneratorSettings.ReportTypes = new[]
    {
        type
    };

    ReportGenerator(pattern, reportsFolder.Combine(subFolder), ReportGeneratorSettings);
}

Setup(ctx =>
{
    if(GitVersion().BranchName == "master")
    {
        publicRelease = true;
        Information("Building a public release.");
    }
    else
    {
        Information("Building a pre-release.");
    }

    Debug("IsLocalBuild: " + BuildSystem.IsLocalBuild);
    Debug("IsRunningOnAppVeyor: " + BuildSystem.IsRunningOnAppVeyor);
    Debug("IsRunningOnAzurePipelines: " + BuildSystem.IsRunningOnAzurePipelines);
    Debug("IsRunningOnAzurePipelinesHosted: " + BuildSystem.IsRunningOnAzurePipelinesHosted);

    Information("Provider: " + BuildSystem.Provider);
});

///////////////////////////////////////////////////////////////////////////////
// TASKS
///////////////////////////////////////////////////////////////////////////////
Task("CleanSolution")
    .Does(() =>
    {
        var solution = ParseSolution(SolutionPath);

        foreach(var project in solution.Projects)
        {
            // check solution items and exclude solution folders, since they are virtual
            if(project.Name == "Solution Items")
                continue;

            var customProject = ParseProject(project.Path, configuration: Configuration, platform: Platform);

            foreach(var path in customProject.OutputPaths)
            {
                CleanDirectory(path.FullPath);
            }
        }

        var folders = new[]
        {
            new DirectoryPath(PackagePath),
            new DirectoryPath(ResultsPath),
        };

        foreach(var folder in folders)
        {
            EnsureDirectoryExists(folder);
            CleanDirectory(folder, (file) => !file.Path.Segments.Last().Contains(".gitignore"));
        }
});

Task("UpdateAssemblyInfo")
    .Does(() =>
    {
        var gitVersion = GitVersion();
        var assemblyInfoParseResult = ParseAssemblyInfo(AssemblyInfoPath);

        var settings = new AssemblyInfoSettings()
        {
            Product                 = assemblyInfoParseResult.Product,
            Company                 = assemblyInfoParseResult.Company,
            Trademark               = assemblyInfoParseResult.Trademark,
            Copyright               = $"© {DateTime.Today.Year} Insire",

            InternalsVisibleTo      = assemblyInfoParseResult.InternalsVisibleTo,

            MetaDataAttributes = new []
            {
                new AssemblyInfoMetadataAttribute()
                {
                    Key = "Platform",
                    Value = Platform,
                },
                new AssemblyInfoMetadataAttribute()
                {
                    Key = "CompileDate",
                    Value = "[UTC]" + DateTime.UtcNow.ToString(),
                },
                new AssemblyInfoMetadataAttribute()
                {
                    Key = "PublicRelease",
                    Value = publicRelease.ToString(),
                },
                new AssemblyInfoMetadataAttribute()
                {
                    Key = "Branch",
                    Value = gitVersion.BranchName,
                },
                new AssemblyInfoMetadataAttribute()
                {
                    Key = "Commit",
                    Value = gitVersion.Sha,
                },
                new AssemblyInfoMetadataAttribute()
                {
                    Key = "Version",
                    Value = GitVersioningGetVersion().SemVer2,
                },
            }
        };

        CreateAssemblyInfo(new FilePath(AssemblyInfoPath), settings);
});

Task("BuildAndPack")
    .DoesForEach(nugetPackageProjects, project=>
    {
        var settings = new ProcessSettings()
            .UseWorkingDirectory(".")
            .WithArguments(builder => builder
                .Append("pack")
                .AppendQuoted(project)
                .Append("--no-restore")
                .Append("--nologo")
                .Append($"-c {Configuration}")
                .Append($"--output \"{PackagePath}\"")
                .Append($"-p:PackageVersion={GitVersioningGetVersion().SemVer2}")
                .Append($"-p:PublicRelease={publicRelease}")

                .Append($"-p:IncludeSymbols=true")
                .Append($"-p:DebugType=portable")
                .Append($"-p:SymbolPackageFormat=snupkg")
                .Append($"-p:SourceLinkCreate=true")
                .Append($"-p:EmbedUntrackedSources=true")
                .Append($"-p:PublishRepositoryUrl=true")
            );

        StartProcess("dotnet", settings);
    });

Task("Test")
    .Does(()=>
    {
        var projectFile = @"./MonstercatNet.Tests/MonstercatNet.Tests.csproj";
        var testSettings = new DotNetCoreTestSettings
        {
            Framework="netcoreapp3.1",
            Configuration = "Release",
            NoBuild = false,
            NoRestore = false,
            ArgumentCustomization = builder => builder
                .Append("--nologo")
                .Append("--results-directory:./Results/coverage")
                .Append($"-p:DebugType=full") // required for opencover codecoverage and sourcelinking
                .Append($"-p:DebugSymbols=true") // required for opencover codecoverage
                .AppendSwitchQuoted("--collect",":","\"\"Code Coverage\"\"")
                .Append($"--logger:trx;LogFileName=..\\{vstestResultsFile.FullPath};"),
        };

        DotNetCoreTest(projectFile, testSettings);
    });

Task("ConvertCoverage")
    .IsDependentOn("Test")
    .WithCriteria(()=> Context.Tools.Resolve("CodeCoverage.exe") != null, $"since CodeCoverage.exe is not a registered tool.")
    .DoesForEach(()=> GetFiles($"{ResultsPath}/coverage/**/*.coverage"), file=>
    {
        var codeCoverageExe = Context.Tools.Resolve("CodeCoverage.exe");
        var result = System.IO.Path.ChangeExtension(file.FullPath, ".xml");

        var settings = new ProcessSettings()
                .UseWorkingDirectory(ResultsPath)
                .WithArguments(builder => builder
                    .Append("analyze")
                    .AppendSwitchQuoted(@"-output",":",result)
                    .Append(file.FullPath)
                );

        StartProcess(codeCoverageExe.FullPath, settings);
    });

Task("CoberturaReport")
    .IsDependentOn("ConvertCoverage")
    .WithCriteria(()=> GetFiles("./Results/coverage/**/*.xml").Count > 0, $"since there is no coverage xml file in /Results/coverage/.")
    .WithCriteria(()=> BuildSystem.IsRunningOnAzurePipelinesHosted, "since task is not running on a Azure Pipelines (Hosted).")
    .Does(()=>
    {
        MergeReports("./Results/coverage/**/*.xml", ReportGeneratorReportType.Cobertura, "cobertura");
    });

Task("HtmlReport")
    .IsDependentOn("ConvertCoverage")
    .WithCriteria(()=> GetFiles("./Results/coverage/**/*.xml").Count > 0, $"since there is no coverage xml file in /Results/coverage/.")
    .WithCriteria(()=> BuildSystem.IsLocalBuild, "since task is not running on a developer machine.")
    .Does(()=>
    {
        MergeReports("./Results/coverage/**/*.xml", ReportGeneratorReportType.Html, "html");
    });

Task("UploadCodecovReport")
     .IsDependentOn("CoberturaReport")
    .WithCriteria(()=> FileExists(coberturaResultFile.FullPath), $"since {coberturaResultFile} wasn't created.")
    .WithCriteria(()=> BuildSystem.IsRunningOnAzurePipelinesHosted, "since task is not running on AzurePipelines (Hosted).")
    .WithCriteria(()=> !string.IsNullOrEmpty(EnvironmentVariable("CODECOV_TOKEN")),"since environment variable CODECOV_TOKEN missing or empty.")
    .Does(()=>
    {
        Codecov(new[]{ coberturaResultFile.FullPath }, EnvironmentVariable("CODECOV_TOKEN"));
    });

Task("TestAndUploadReport")
    .IsDependentOn("HtmlReport")
    .IsDependentOn("UploadCodecovReport");

Task("PushLocally")
    .WithCriteria(() => BuildSystem.IsLocalBuild,"since task is not running on a developer machine.")
    .WithCriteria(() => DirectoryExists(localNugetDirectory), $@"since there is no local directory ({localNugetDirectory}) to push nuget packages to.")
    .DoesForEach(() => GetFiles(PackagePath + "/*.nupkg"), path =>
    {
        var settings = new ProcessSettings()
            .UseWorkingDirectory(".")
            .WithArguments(builder => builder
            .Append("push")
            .AppendSwitchQuoted("-source", localNugetDirectory)
            .AppendQuoted(path.FullPath));

        StartProcess("./tools/nuget.exe",settings);
    });

Task("Default")
    .IsDependentOn("CleanSolution")
    .IsDependentOn("UpdateAssemblyInfo")
    .IsDependentOn("TestAndUploadReport")
    .IsDependentOn("BuildAndPack")
    .IsDependentOn("PushLocally");

RunTarget(Argument("target", "Default"));
