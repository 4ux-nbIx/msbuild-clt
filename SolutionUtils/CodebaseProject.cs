namespace MsBuild.Utils
{
    #region Namespace Imports

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using JetBrains.Annotations;

    using Microsoft.Build.Evaluation;

    #endregion


    public class CodebaseProject
    {
        private readonly Codebase _codebase;
        private readonly ILogger _logger;
        private readonly Project _project;

        [CanBeNull]
        private List<CodebaseProject> _referencedProjects;

        private List<Solution> _solutions = new List<Solution>();

        internal CodebaseProject(Codebase codebase, Project project, ILogger logger)
        {
            _codebase = codebase;
            _project = project;
            _logger = logger;

            Guid = _project.GetProjectGuid();
            Name = Path.GetFileNameWithoutExtension(project.FullPath.GetFileSystemName(out _));
        }

        public string DirectoryPath => _project.DirectoryPath;

        public string FullPath => _project.FullPath;
        public Guid Guid { get; }
        public bool IsDirty => _project.IsDirty;
        public string Name { get; }

        public string SolutionProjectTypeGuid
        {
            get
            {
                var extension = Path.GetExtension(_project.FullPath)?.ToLowerInvariant();

                switch (extension)
                {
                    case ".sqlproj":
                        return "{00D1A9C2-B5F0-4AF3-8072-F6C62B433612}";

                    case ".csproj":
                        return "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";

                    default:
                        return null;
                }
            }
        }

        public IReadOnlyList<Solution> Solutions => _solutions;

        public IList<ProjectItem> AddItem(string itemType, string unevaluatedInclude, IEnumerable<KeyValuePair<string, string>> metadata) =>
            _project.AddItem(itemType, unevaluatedInclude, metadata);

        public IList<ProjectItem> AddItem(string itemType, string unevaluatedInclude) => _project.AddItem(itemType, unevaluatedInclude);

        public IEnumerable<CodebaseProject> GetAllReferencedProjects() =>
            GetReferencedProjects().SelectMany(p => p.GetAllReferencedProjects()).Distinct();

        public ICollection<ProjectItem> GetItems(string itemType) => _project.GetItems(itemType);

        public ProjectProperty GetProperty(string name) => _project.GetProperty(name);

        public List<CodebaseProject> GetReferencedProjects()
        {
            if (_referencedProjects != null)
            {
                return _referencedProjects;
            }

            _referencedProjects = _project.GetProjectReferences()
                .Select(i => Path.GetFullPath(Path.Combine(_project.DirectoryPath, i.EvaluatedInclude)))
                .Select(p => _codebase.LoadProject(p))
                .Where(p => p != null)
                .Distinct()
                .ToList();

            foreach (var project in _referencedProjects)
            {
                project._solutions.AddRange(_solutions.Except(project._solutions));
            }

            return _referencedProjects;
        }

        public Uri GetRelativePath(Solution solution)
        {
            var projectFullPath = _project.FullPath.GetFileSystemPath();
            var projectUri = new Uri(projectFullPath);

            return new Uri(solution.FullPath).MakeRelativeUri(projectUri);
        }

        public void Save()
        {
            _project.Save();
        }

        internal void AddSolution(Solution solution)
        {
            _solutions.Add(solution);
        }
    }
}