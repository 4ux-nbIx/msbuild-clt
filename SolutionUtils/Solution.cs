namespace MsBuild.Utils
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
        private readonly SolutionFile _file;
        private readonly string _fullPath;
        private readonly ILogger _logger;

        [CanBeNull]
        private List<Project> _projects;

        internal Solution(string fullPath, Codebase codebase, ILogger logger)
        {
            _file = SolutionFile.Parse(fullPath);
            _fullPath = fullPath;
            _codebase = codebase;
            _logger = logger;
        }

        public string FullPath => _fullPath;

        public void FixProjectReferences()
        {
            var missingProjects = _file.GetMsBuildProjects().Where(p => !File.Exists(p.AbsolutePath)).ToList();

            var removedProjects = missingProjects.Where(p => !_codebase.ProjectsByGuid.ContainsKey(Guid.Parse(p.ProjectGuid))).ToList();
            var movedProjects = missingProjects.Except(removedProjects).ToList();
            var changedProjectGuids = new List<Tuple<string, string>>();

            foreach (var projectInSolution in removedProjects.ToList())
            {
                var fileName = Path.GetFileName(projectInSolution.AbsolutePath);

                var projects = _codebase.FindProjectsByFileName(fileName);

                if (projects.Count == 1)
                {
                    var newGuid = projects[0].Guid.ToString("B").ToUpperInvariant();
                    changedProjectGuids.Add(Tuple.Create(projectInSolution.ProjectGuid, newGuid));

                    removedProjects.Remove(projectInSolution);
                    movedProjects.Add(projectInSolution);
                    projectInSolution.UpdateProjectGuid(newGuid);
                }

                // TODO: log warning
            }

            foreach (var project in GetAllProjects())
            {
                project.FixProjectReferences();
            }

            var fileProjectsByGuid = _file.ProjectsByGuid.ToDictionary(p => Guid.Parse(p.Key), p => p.Value);
            var newProjects = GetAllProjects(true).Where(p => !fileProjectsByGuid.ContainsKey(p.Guid)).ToList();

            if (removedProjects.Any() || movedProjects.Any())
            {
                this.Update(removedProjects, movedProjects, newProjects, changedProjectGuids, _codebase);
            }
        }

        public IEnumerable<Project> GetAllProjects(bool includeUnsupported = false) =>
            GetProjects()
                .Where(p => includeUnsupported || !p.IsNotSupported)
                .SelectMany(p => p.GetAllReferencedProjects(includeUnsupported))
                .Distinct();

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
    }
}