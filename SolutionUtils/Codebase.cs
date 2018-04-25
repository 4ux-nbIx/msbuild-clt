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
        private string _projectReferenceItemType = "ProjectReference";

        private Codebase(string folder, IEnumerable<string> solutions, ILogger logger)
        {
            _folder = folder;
            _logger = logger;
            Solutions = solutions.Select(f => SolutionFile.Parse(f)).ToList();

            _projectCollection = new ProjectCollection(ToolsetDefinitionLocations.Registry);
        }

        public Dictionary<string, Project> ProjectsByFileName { get; } = new Dictionary<string, Project>();
        public Dictionary<Guid, Project> ProjectsByGuid { get; } = new Dictionary<Guid, Project>();
        public Dictionary<Guid, Project> ProjectsWithDuplicateGuid { get; } = new Dictionary<Guid, Project>();

        public List<SolutionFile> Solutions { get; set; }

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
                FixProjectReferences(solution);
            }
        }

        public IEnumerable<Project> GetAllProjects()
        {
            var projects = new List<Project>();

            foreach (var projectInSolution in Solutions.SelectMany(s => s.GetMsBuildProjects()))
            {
                var project = LoadProject(projectInSolution);

                if (project == null)
                {
                    continue;
                }

                projects.Add(project);

                projects.AddRange(GetReferencedProjects(project, true));
            }

            return projects.Distinct();
        }

        private void FixProjectReferences(SolutionFile solution)
        {
            var missingProjects = solution.GetMsBuildProjects().Where(p => !File.Exists(p.AbsolutePath)).ToList();

            var removedProjects = missingProjects.Where(p => !ProjectsByGuid.ContainsKey(Guid.Parse(p.ProjectGuid))).ToList();
            var movedProjects = missingProjects.Except(removedProjects).ToList();
            var changedProjectGuids = new List<Tuple<string, string>>();

            foreach (var projectInSolution in removedProjects.ToList())
            {
                var fileName = Path.GetFileName(projectInSolution.AbsolutePath);

                var projects = ProjectsByFileName
                    .Where(p => Path.GetFileName(p.Key)?.Equals(fileName, StringComparison.OrdinalIgnoreCase) == true)
                    .Select(p => p.Value)
                    .ToList();

                if (projects.Count == 1)
                {
                    var newGuid = projects[0].GetProjectGuid().ToString("B").ToUpperInvariant();
                    changedProjectGuids.Add(Tuple.Create(projectInSolution.ProjectGuid, newGuid));

                    removedProjects.Remove(projectInSolution);
                    movedProjects.Add(projectInSolution);
                    projectInSolution.UpdateProjectGuid(newGuid);
                }

                // TODO: log warning
            }

            // TODO: try to find missing projects by file name

            if (removedProjects.Any() || movedProjects.Any())
            {
                solution.Update(removedProjects, movedProjects, null, changedProjectGuids, this);
            }
        }

        private IEnumerable<Project> GetReferencedProjects(Project project, bool recursive)
        {
            var projects = project.GetItemsIgnoringCondition(_projectReferenceItemType)
                .Select(i => Path.GetFullPath(Path.Combine(project.DirectoryPath, i.EvaluatedInclude)))
                .Select(LoadProject)
                .Where(p => p != null)
                .Distinct()
                .ToList();

            if (recursive)
            {
                return projects.Concat(projects.SelectMany(p => GetReferencedProjects(p, true))).Distinct();
            }

            return projects;
        }

        [CanBeNull]
        private Project LoadProject(ProjectInSolution projectInSolution) => LoadProject(projectInSolution.AbsolutePath);

        [CanBeNull]
        private Project LoadProject(string absolutePath)
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
                project = new Project(
                    absolutePath,
                    _projectCollection.GlobalProperties,
                    _projectCollection.DefaultToolsVersion,
                    _projectCollection,
                    ProjectLoadSettings.IgnoreEmptyImports
                    | ProjectLoadSettings.IgnoreInvalidImports
                    | ProjectLoadSettings.IgnoreMissingImports);
            }
            catch (Exception exception)
            {
                _logger.WriteWarning($"Failed to load {absolutePath}\n{exception.Message}");
                return null;
            }

            ProjectsByFileName.Add(absolutePath, project);

            var guid = project.GetProjectGuid();

            if (ProjectsByGuid.ContainsKey(guid))
            {
                ProjectsWithDuplicateGuid.Add(guid, project);
            }
            else
            {
                ProjectsByGuid.Add(guid, project);
            }

            return project;
        }
    }
}