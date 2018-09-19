namespace MsBuild.Clt
{
    #region Namespace Imports

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using McMaster.Extensions.CommandLineUtils;

    #endregion


    internal static class Program
    {
        private const string _helpOptionTemplate = "-?|-h|--help";
        private static ILogger _logger;

        private static void ConfigureFindContainingSolutionsCommand(CommandLineApplication command)
        {
            command.Setup("Finds solutions containing the specified files.");

            var workspacePath = command.CreateWorkspacePathArgument();
            var filesOption = command.Option<string>("--containing-files", "file list", CommandOptionType.SingleValue);
            var excludedSolutions = command.CreateExcludeOption();

            command.OnExecute(
                () =>
                {
                    var files = filesOption.ParseList().Select(f => f.ToFullPath(workspacePath.Value)).ToList();

                    var codebase = Codebase.CreateFromFolder(workspacePath.Value, _logger);

                    var solutions = codebase.GetSolutions(excludedSolutions.ParsedValues)
                        .Where(s => files.Any(f => s.ContainsFile(f)))
                        .ToList();

                    foreach (var solution in solutions)
                    {
                        _logger.WriteInfo(solution.FullPath);
                    }

                    return 0;
                });
        }

        private static void ConfigureFindUnreferencedProjectsCommand(CommandLineApplication command)
        {
            command.Setup("Finds unreferenced projects in the specified folder.");

            var workspacePath = command.CreateWorkspacePathArgument();

            command.OnExecute(
                () =>
                {
                    foreach (var project in Codebase.CreateFromFolder(workspacePath.Value, _logger).FindUnreferencedProjects())
                    {
                        _logger.WriteInfo(project);
                    }

                    return 0;
                });
        }

        private static void ConfigureFixAssemblyBindingsCommand(CommandLineApplication command)
        {
            command.Setup("Fixes assembly bindings.");

            var solutionPath = command.CreateSolutionArgument();

            command.OnExecute(
                () =>
                {
                    var assemblyLoader = new AssemblyLoader(_logger);
                    var bindingsUtil = new AssemblyBindingsUtil(_logger);

                    var projects = Codebase.CreateFromSolution(solutionPath.Value, _logger).GetAllProjects().ToList();

                    for (var index = 0; index < projects.Count; index++)
                    {
                        var project = projects[index];
                        _logger.WriteInfo($"({index + 1}/{projects.Count}) Processing {project.FullPath}");

                        var outputPathProperty = project.GetProperty("OutputPath");
                        string outputPath;

                        if (outputPathProperty == null)
                        {
                            _logger.WriteWarning("OutputPath property not set. Assuming 'bin\\Debug'");
                            outputPath = "bin\\Debug";
                        }
                        else
                        {
                            outputPath = outputPathProperty.EvaluatedValue;
                        }

                        assemblyLoader.Load(Path.Combine(project.DirectoryPath, outputPath));

                        if (assemblyLoader.AssemblyNames.Count == 0)
                        {
                            _logger.WriteWarning("No binaries found on output path. Skipping project...");
                            continue;
                        }

                        var webProjectGuids = new List<string>
                        {
                            "{E24C65DC-7377-472B-9ABA-BC803B73C61A}",
                            "{349C5851-65DF-11DA-9384-00065B846F21}",
                            "{E3E379DF-F4C6-4180-9B81-6769533ABE47}",
                            "{E53F8FEA-EAE0-44A6-8774-FFD645390401}",
                            "{F85E285D-A4E0-4152-9332-AB1D724D3325}",
                            "{603C0E0B-DB56-11DC-BE95-000D561079B0}",
                            "{8BB2217D-0F2D-49D1-97BC-3654ED321F3B}"
                        };

                        var projectTypes = project.GetProperty(@"ProjectTypeGuids")?.EvaluatedValue;

                        var isWebProject = !string.IsNullOrEmpty(projectTypes)
                                           && webProjectGuids.Any(t => projectTypes.ToUpperInvariant().Contains(t));

                        string configName;

                        if (isWebProject)
                        {
                            configName = @"Web.config";

                            var configItem = project.GetItems("Content")
                                .FirstOrDefault(i => string.Equals(i.EvaluatedInclude, configName, StringComparison.OrdinalIgnoreCase));

                            if (configItem == null)
                            {
                                project.AddItem("Content", configName, new Dictionary<string, string> { { "SubType", "Designer" } });
                            }
                        }
                        else
                        {
                            configName = @"App.config";

                            var configItem = project.GetItems("None")
                                .FirstOrDefault(i => string.Equals(i.EvaluatedInclude, configName, StringComparison.OrdinalIgnoreCase));

                            if (configItem == null)
                            {
                                project.AddItem("None", configName);
                            }
                        }

                        var appConfigPath = Path.Combine(project.DirectoryPath, configName);

                        if (bindingsUtil.Update(appConfigPath, assemblyLoader.AssemblyNames) && project.IsDirty)
                        {
                            project.Save();
                        }
                    }

                    return 0;
                });
        }

        private static void ConfigureFixProjectReferencesCommand(CommandLineApplication command)
        {
            command.Setup("Fixes project references.");

            var workspacePath = command.CreateWorkspacePathArgument();
            var excludedSolutions = command.CreateExcludeOption();

            command.OnExecute(
                () =>
                {
                    var codebase = Codebase.CreateFromFolder(workspacePath.Value, _logger);
                    codebase.FixProjectReferences(excludedSolutions.ParseList());
                });
        }

        private static void ConfigureMergeSolutionsCommand(CommandLineApplication command)
        {
            command.Setup("Merges solution files.");

            var workspacePath = command.CreateWorkspacePathArgument();
            var targetSolution = command.CreateSolutionArgument();
            var excludedSolutions = command.CreateExcludeOption();

            command.OnExecute(
                () =>
                {
                    var codebase = Codebase.CreateFromFolder(workspacePath.Value, _logger);
                    codebase.MergeSolutions(targetSolution.Value, excludedSolutions.ParsedValues);
                });
        }

        private static CommandOption<string> CreateExcludeOption(this CommandLineApplication command) =>
            command.Option<string>("--excluding <EXCLUDE>", "Solution files to exclude", CommandOptionType.MultipleValue);

        private static CommandArgument CreateSolutionArgument(this CommandLineApplication command) =>
            command.Argument("solution", "Solution file path.");

        private static CommandArgument CreateWorkspacePathArgument(this CommandLineApplication command) =>
            command.Argument("workspace", "Workspace folder path");


        private static void Main(string[] args)
        {
            _logger = new ConsoleLogger();

            var application = new CommandLineApplication { Name = "msbuild-clt" };
            application.Setup("MSBuild command line toolkit");

            application.OnExecute(
                () =>
                {
                    _logger.WriteInfo(application.GetFullNameAndVersion());
                    _logger.WriteInfo(application.GetHelpText());

                    return 0;
                });

            application.Command(
                "find",
                command =>
                {
                    command.Setup("Search for projects, solutions, files and etc.");

                    command.Command("unreferenced-projects", ConfigureFindUnreferencedProjectsCommand);
                    command.Command("solutions",             ConfigureFindContainingSolutionsCommand);
                });

            application.Command(
                "fix",
                command =>
                {
                    command.Setup("Fix broken file/project references and etc.");

                    command.Command("assembly-bindings",  ConfigureFixAssemblyBindingsCommand);
                    command.Command("project", COnfigureFixProjectCommand);
                });

            application.Command(
                "merge",
                command =>
                {
                    command.Setup("Merge solutions");

                    command.Command("solutions", ConfigureMergeSolutionsCommand);
                });

            application.Execute(args);
        }

        private static void COnfigureFixProjectCommand(CommandLineApplication command)
        {
            command.Setup("Project fixes");

            command.Command("references", ConfigureFixProjectReferencesCommand);
        }

        private static IReadOnlyList<string> ParseList(this CommandOption<string> filesOption) =>
            filesOption.ParsedValues.Where(v => !string.IsNullOrWhiteSpace(v)).SelectMany(v => v.ParseList()).ToList();

        private static IEnumerable<string> ParseList(this string value) =>
            value.Trim().Trim('"').Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()).Distinct();

        private static void Setup(this CommandLineApplication command, string description)
        {
            command.Description = description;
            command.HelpOption(_helpOptionTemplate);
        }
    }
}