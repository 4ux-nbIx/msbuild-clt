namespace MsBuild.Clt
{
    #region Namespace Imports

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using JetBrains.Annotations;

    using Microsoft.Build.Construction;
    using Microsoft.Build.Evaluation;
    using Microsoft.Build.Globbing;

    #endregion


    internal class Codebase
    {
        private readonly ILogger _logger;
        private string _folder;
        private ProjectCollection _projectCollection;
        private List<Solution> _solutions = new List<Solution>();

        private Codebase(string folder, IEnumerable<string> solutions, ILogger logger)
        {
            _folder = folder;
            _logger = logger;

            foreach (var solution in solutions)
            {
                LoadSolution(solution);
            }

            _projectCollection = new ProjectCollection(ToolsetDefinitionLocations.Registry);

            var latestToolset = _projectCollection.Toolsets.Select(t => (Version: new Version(t.ToolsVersion), Toolset: t))
                .OrderByDescending(t => t.Version)
                .FirstOrDefault()
                .Toolset;

            if (latestToolset != null)
            {
                _projectCollection.DefaultToolsVersion = latestToolset.ToolsVersion;
            }
        }

        public Dictionary<string, Project> ProjectsByFileName { get; } = new Dictionary<string, Project>();
        public Dictionary<Guid, Project> ProjectsByGuid { get; } = new Dictionary<Guid, Project>();
        public Dictionary<Guid, Project> ProjectsWithDuplicateGuid { get; } = new Dictionary<Guid, Project>();

        public IReadOnlyCollection<Solution> Solutions => _solutions;

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

        public List<Project> FindProjectsByFileName(string fileName)
        {
            return ProjectsByFileName.Where(p => Path.GetFileName(p.Key)?.Equals(fileName, StringComparison.OrdinalIgnoreCase) == true)
                .Select(p => p.Value)
                .ToList();
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

        public void FixProjectReferences(IReadOnlyList<string> excludedSolutions)
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

            var solutions = GetSolutions(excludedSolutions);

            foreach (var solution in solutions)
            {
                solution.FixProjectReferences();
            }
        }

        public IEnumerable<Project> GetAllProjects()
        {
            return Solutions.SelectMany(s => s.GetAllProjects()).Distinct();
        }

        public List<Solution> GetSolutions(IReadOnlyList<string> excludedSolutions)
        {
            if (excludedSolutions == null || excludedSolutions.Count == 0)
            {
                return Solutions.ToList();
            }

            var excludeGlob = new CompositeGlob(excludedSolutions.Select(s => MSBuildGlob.Parse(_folder, s)));
            return Solutions.Where(s => !excludeGlob.IsMatch(s.FullPath)).ToList();
        }

        public void MergeSolutions(string destinationSolutionPath, IReadOnlyList<string> excludedSolutions)
        {
            var solutionsToMerge = GetSolutions(excludedSolutions);

            if (solutionsToMerge.Count == 0)
            {
                _logger.WriteInfo("No solutions found.");
            }

            var fullSolutionPath = Path.Combine(_folder, destinationSolutionPath);

            var targetSolution =
                Solutions.FirstOrDefault(s => s.FullPath.Equals(fullSolutionPath, StringComparison.InvariantCultureIgnoreCase));

            if (targetSolution == null)
            {
                var solution = solutionsToMerge[0];
                solutionsToMerge.RemoveAt(0);

                File.Copy(solution.FullPath, fullSolutionPath, false);

                targetSolution = LoadSolution(fullSolutionPath);
            }

            foreach (var solution in solutionsToMerge.Where(s => s != targetSolution))
            {
                targetSolution.Merge(solution);
            }
        }

        [CanBeNull]
        internal Project LoadProject(ProjectInSolution projectInSolution) =>
            LoadProject(projectInSolution.AbsolutePath, Guid.Parse(projectInSolution.ProjectGuid));

        [CanBeNull]
        internal Project LoadProject(string absolutePath, Guid guid = default(Guid))
        {
            absolutePath = Path.GetFullPath(absolutePath).ToLowerInvariant();

            Project project;

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
                var msbuildProject = new Microsoft.Build.Evaluation.Project(
                    absolutePath,
                    _projectCollection.GlobalProperties,
                    _projectCollection.DefaultToolsVersion,
                    _projectCollection,
                    ProjectLoadSettings.IgnoreEmptyImports
                    | ProjectLoadSettings.IgnoreInvalidImports
                    | ProjectLoadSettings.IgnoreMissingImports);

                project = new Project(this, msbuildProject, guid, _logger);
            }
            catch (Exception exception)
            {
                _logger.WriteWarning($"Failed to load {absolutePath}\n{exception.Message}");
                project = new Project(this, absolutePath, guid, _logger);
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

        private Solution LoadSolution(string f)
        {
            var solution = new Solution(f, this, _logger);

            _solutions.Add(solution);

            return solution;
        }
    }
}