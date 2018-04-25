namespace MsBuild.Utils
{
    #region Namespace Imports

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using JetBrains.Annotations;

    using Microsoft.Build.Construction;
    using Microsoft.Build.Evaluation;

    #endregion


    internal class Codebase
    {
        private readonly ILogger _logger;
        private string _folder;
        private ProjectCollection _projectCollection;

        private Codebase(string folder, IEnumerable<string> solutions, ILogger logger)
        {
            _folder = folder;
            _logger = logger;
            Solutions = solutions.Select(f => new Solution(f, this, logger)).ToList();

            _projectCollection = new ProjectCollection(ToolsetDefinitionLocations.Registry);
        }

        public Dictionary<string, CodebaseProject> ProjectsByFileName { get; } = new Dictionary<string, CodebaseProject>();
        public Dictionary<Guid, CodebaseProject> ProjectsByGuid { get; } = new Dictionary<Guid, CodebaseProject>();
        public Dictionary<Guid, CodebaseProject> ProjectsWithDuplicateGuid { get; } = new Dictionary<Guid, CodebaseProject>();

        public List<Solution> Solutions { get; set; }

        public static Codebase CreateFromFolder(string folder, ILogger logger)
        {
            var solutions = Directory.EnumerateFileSystemEntries(folder, "*.sln", SearchOption.AllDirectories);

            return new Codebase(folder, solutions, logger);
        }

        public static Codebase CreateFromSolution(string solution, ILogger logger)
        {
            var folder = Path.GetDirectoryName(solution);

            return new Codebase(folder, new[] { solution }, logger);
        }

        public IEnumerable<string> FindUnreferencedProjects()
        {
            var projectExtensions = GetAllProjects().Select(p => Path.GetExtension(p.FullPath)).Distinct().ToList();

            return projectExtensions.SelectMany(e => Directory.EnumerateFileSystemEntries(_folder, $"*{e}", SearchOption.AllDirectories))
                .Select(f => f.ToLowerInvariant())
                .Distinct()
                .Except(ProjectsByFileName.Keys)
                .OrderBy(f => f);
        }

        public void FixProjectReferences()
        {
            var unreferencedProjects = FindUnreferencedProjects().ToList();

            foreach (var unreferencedProject in unreferencedProjects)
            {
                try
                {
                    LoadProject(unreferencedProject);
                }
                catch
                {
                    _logger.WriteWarning($"Failed to load unreferenced project: {unreferencedProject}");
                }
            }

            foreach (var solution in Solutions)
            {
                solution.FixProjectReferences();
            }
        }

        public IEnumerable<CodebaseProject> GetAllProjects()
        {
            return Solutions.SelectMany(s => s.GetAllProjects()).Distinct();
        }

        [CanBeNull]
        internal CodebaseProject LoadProject(ProjectInSolution projectInSolution) => LoadProject(projectInSolution.AbsolutePath);

        [CanBeNull]
        internal CodebaseProject LoadProject(string absolutePath)
        {
            absolutePath = Path.GetFullPath(absolutePath).ToLowerInvariant();

            CodebaseProject project;

            if (ProjectsByFileName.TryGetValue(absolutePath, out project))
            {
                return project;
            }

            if (!File.Exists(absolutePath))
            {
                return null;
            }

            try
            {
                var msbuildProject = new Project(
                    absolutePath,
                    _projectCollection.GlobalProperties,
                    _projectCollection.DefaultToolsVersion,
                    _projectCollection,
                    ProjectLoadSettings.IgnoreEmptyImports
                    | ProjectLoadSettings.IgnoreInvalidImports
                    | ProjectLoadSettings.IgnoreMissingImports);

                project = new CodebaseProject(this, msbuildProject, _logger);
            }
            catch (Exception exception)
            {
                _logger.WriteWarning($"Failed to load {absolutePath}\n{exception.Message}");
                return null;
            }

            ProjectsByFileName.Add(absolutePath, project);

            if (ProjectsByGuid.ContainsKey(project.Guid))
            {
                ProjectsWithDuplicateGuid.Add(project.Guid, project);
            }
            else
            {
                ProjectsByGuid.Add(project.Guid, project);
            }

            return project;
        }
    }
}