namespace MsBuild.Clt
{
    #region Namespace Imports

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using JetBrains.Annotations;

    using Microsoft.Build.Construction;

    #endregion


    public class Solution
    {
        private readonly Codebase _codebase;
        private readonly ILogger _logger;
        private SolutionFile _file;

        [CanBeNull]
        private List<Project> _projects;

        internal Solution(string fullPath, Codebase codebase, ILogger logger)
        {
            _file = SolutionFile.Parse(fullPath);
            FullPath = fullPath;
            _codebase = codebase;
            _logger = logger;
        }

        public string FileName => Path.GetFileName(FullPath);
        public string FullPath { get; }

        public bool ContainsFile(string filePath)
        {
            if (_file.ProjectsInOrder.Any(p => p.AbsolutePath == filePath))
            {
                return true;
            }

            return GetAllProjects().Any(p => p.ContainsFile(filePath));
        }

        public void FixProjectReferences()
        {
            var projectsByGuid = _file.GetMsBuildProjects().ToDictionary(p => Guid.Parse(p.ProjectGuid), p => p);
            var missingProjects = _file.GetMsBuildProjects().Where(p => !File.Exists(p.AbsolutePath)).ToList();

            var removedProjects = missingProjects.Where(p => !_codebase.ProjectsByGuid.ContainsKey(Guid.Parse(p.ProjectGuid))).ToList();
            var movedProjects = missingProjects.Except(removedProjects).ToList();

            var projectsWithNewGuids = projectsByGuid.Where(p => !_codebase.ProjectsByGuid.ContainsKey(p.Key))
                .Select(p => p.Value)
                .Except(removedProjects)
                .ToList();

            var changedProjectGuids = new Dictionary<string, string>();

            foreach (var projectInSolution in removedProjects.Concat(projectsWithNewGuids).ToList())
            {
                var fileName = Path.GetFileName(projectInSolution.AbsolutePath);

                var projects = _codebase.FindProjectsByFileName(fileName);

                if (projects.Count == 1)
                {
                    var newGuid = projects[0].Guid.ToSolutionProjectGuid();
                    changedProjectGuids.Add(projectInSolution.ProjectGuid, newGuid);

                    removedProjects.Remove(projectInSolution);
                    movedProjects.Add(projectInSolution);
                    projectInSolution.UpdateProjectGuid(newGuid);
                }
                else if (projects.Count > 0)
                {
                    _logger.WriteWarning(
                        $"Could not resolve project reference '{projectInSolution.ProjectName}' in solution '{FullPath}': project with same GUID not found, but found {projects.Count} projects by file name.");
                }
            }

            foreach (var project in GetAllProjects())
            {
                project.FixProjectReferences();
            }

            var allProjects = GetAllProjects(true).ToList();

            var newProjects = allProjects.Where(
                    p => !projectsByGuid.ContainsKey(p.Guid) && !changedProjectGuids.Values.Contains(p.Guid.ToSolutionProjectGuid()))
                .Distinct()
                .ToList();

            Update(removedProjects, movedProjects, newProjects, changedProjectGuids);
        }


        public IEnumerable<Project> GetAllProjects(bool includeUnsupported = false)
        {
            var projects = GetProjects().Where(p => includeUnsupported || !p.IsNotSupported).ToList();

            return projects.Concat(projects.SelectMany(p => p.GetAllReferencedProjects(includeUnsupported))).Distinct();
        }

        public List<Project> GetProjects()
        {
            if (_projects != null)
            {
                return _projects;
            }

            _projects = new List<Project>();

            foreach (var projectInSolution in _file.GetMsBuildProjects())
            {
                var project = _codebase.LoadProject(projectInSolution);

                if (project == null)
                {
                    continue;
                }

                project.AddSolution(this);

                _projects.Add(project);
            }

            return _projects;
        }

        public void Merge(Solution solution)
        {
            var existingProjects = _file.ProjectsInOrder.Select(p => Guid.Parse(p.ProjectGuid)).ToList();

            var newProjects = solution.GetProjects().Where(p => !existingProjects.Contains(p.Guid)).ToList();

            Update(null, null, newProjects, null);
        }

        public override string ToString() => $"{nameof(FileName)}: {FileName}, {nameof(FullPath)}: {FullPath}";

        private void Update(
            [CanBeNull] List<ProjectInSolution> removedProjects,
            [CanBeNull] List<ProjectInSolution> updatedProjects,
            [CanBeNull] List<Project> newProjects,
            [CanBeNull] Dictionary<string, string> changedProjectGuids)
        {
            if ((removedProjects == null || removedProjects.Count == 0)
                && (updatedProjects == null || updatedProjects.Count == 0)
                && (newProjects == null || newProjects.Count == 0)
                && (changedProjectGuids == null || changedProjectGuids.Count == 0))
            {
                return;
            }

            var path = FullPath;

            var newLines = this.Update(
                    File.ReadAllLines(path),
                    removedProjects,
                    updatedProjects,
                    newProjects,
                    changedProjectGuids,
                    _codebase)
                .ToList();

            File.WriteAllLines(path, newLines);

            _file = SolutionFile.Parse(FullPath);

            _projects = null;
        }
    }
}