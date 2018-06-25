namespace MsBuild.Utils
{
    #region Namespace Imports

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using McMaster.Extensions.CommandLineUtils;

    #endregion


    internal class Program
    {
        private static string _helpOptionTemplate;
        private static ILogger _logger;

        private static void ConfigureFindUnreferencedProjectsCommand(CommandLineApplication command)
        {
            command.Description = "Finds unreferenced projects in the specified folder.";
            command.HelpOption(_helpOptionTemplate);

            var sourceCodeFolderArgument = command.Argument("folder", "Source code folder");

            command.OnExecute(
                () =>
                {
                    foreach (var project in Codebase.CreateFromFolder(sourceCodeFolderArgument.Value, _logger).FindUnreferencedProjects())
                    {
                        _logger.WriteInfo(project);
                    }

                    return 0;
                });
        }

        private static void ConfigureFixAssemblyBindingsCommand(CommandLineApplication command)
        {
            command.Description = "Fixes assembly bindings.";
            command.HelpOption(_helpOptionTemplate);

            var solutionArgument = command.Argument("solution", "Solution file");

            command.OnExecute(
                () =>
                {
                    var assemblyLoader = new AssemblyLoader(_logger);
                    var bindingsUtil = new AssemblyBindingsUtil(_logger);

                    var projects = Codebase.CreateFromSolution(solutionArgument.Value, _logger).GetAllProjects().ToList();

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
            command.Description = "Fixes project references.";
            command.HelpOption(_helpOptionTemplate);

            var pathArgument = command.Argument("path", "Worspace of solution path");

            command.OnExecute(
                () =>
                {
                    var path = pathArgument.Value;

                    var codebase = Codebase.CreateFromFolder(path, _logger);
                    codebase.FixProjectReferences();
                });
        }

        private static void ConfigureMergeSolutionsCommand(CommandLineApplication command)
        {
            command.Description = "Merges solution files.";
            command.HelpOption(_helpOptionTemplate);

            var pathArgument = command.Argument("path", "Worspace folder path");
            var targetArgument = command.Argument("target-solution", "Target solution file name.");
            var excludeOption = command.Option<string>("--exclude <EXCLUDE>", "Solution files to exclude", CommandOptionType.MultipleValue);

            command.OnExecute(
                () =>
                {
                    var path = pathArgument.Value;

                    var codebase = Codebase.CreateFromFolder(path, _logger);
                    codebase.MergeSolutions(targetArgument.Value, excludeOption.ParsedValues);
                });
        }

        private static void Main(string[] args)
        {
            _logger = new ConsoleLogger();

            var application = new CommandLineApplication { Name = "MsBuildUtils" };
            _helpOptionTemplate = "-?|-h|--help";
            application.HelpOption(_helpOptionTemplate);

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
                    command.Description = "TODO";
                    command.HelpOption(_helpOptionTemplate);

                    command.Command("unreferenced-projects", ConfigureFindUnreferencedProjectsCommand);
                });

            application.Command(
                "fix",
                command =>
                {
                    command.Description = "TODO";
                    command.HelpOption(_helpOptionTemplate);

                    command.Command("assembly-bindings",  ConfigureFixAssemblyBindingsCommand);
                    command.Command("project-references", ConfigureFixProjectReferencesCommand);
                });

            application.Command(
                "merge",
                command =>
                {
                    command.Description = "TODO";
                    command.HelpOption(_helpOptionTemplate);

                    command.Command("solutions", ConfigureMergeSolutionsCommand);
                });

            application.Execute(args);
        }
    }
}