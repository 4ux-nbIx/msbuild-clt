namespace MsBuild.Utils
{
    #region Namespace Imports

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    using JetBrains.Annotations;

    using Microsoft.Build.Evaluation;

    using MsBuildProject = Microsoft.Build.Evaluation.Project;

    #endregion


    public class Project
    {
        private readonly Codebase _codebase;
        private readonly ILogger _logger;

        [CanBeNull]
        private readonly MsBuildProject _project;

        [CanBeNull]
        private List<Project> _referencedProjects;

        private List<Solution> _solutions = new List<Solution>();

        internal Project(Codebase codebase, MsBuildProject project, ILogger logger)
            : this(codebase, project.FullPath.GetFileSystemPath(), project.GetProjectGuid(), logger) =>
            _project = project;

        internal Project(Codebase codebase, string absolutePath, Guid guid, ILogger logger)
        {
            _codebase = codebase;
            _logger = logger;

            FullPath = absolutePath;
            Guid = guid;
            Name = Path.GetFileNameWithoutExtension(absolutePath);
        }

        public string DirectoryPath => _project?.DirectoryPath ?? Path.GetDirectoryName(FullPath);
        public string FullPath { get; }
        public Guid Guid { get; }
        public bool IsDirty => _project?.IsDirty ?? false;

        public bool IsNotSupported => _project == null;
        public string Name { get; }

        public string SolutionProjectTypeGuid
        {
            get
            {
                var extension = Path.GetExtension(FullPath)?.ToLowerInvariant();

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
            GetProject().AddItem(itemType, unevaluatedInclude, metadata);

        public IList<ProjectItem> AddItem(string itemType, string unevaluatedInclude) => GetProject().AddItem(itemType, unevaluatedInclude);

        public IEnumerable<Project> GetAllReferencedProjects() =>
            GetReferencedProjects().Where(p => !p.IsNotSupported).SelectMany(p => p.GetAllReferencedProjects()).Distinct();

        public ICollection<ProjectItem> GetItems(string itemType) => GetProject().GetItems(itemType);

        public ProjectProperty GetProperty(string name) => GetProject().GetProperty(name);

        public List<Project> GetReferencedProjects()
        {
            if (_referencedProjects != null)
            {
                return _referencedProjects;
            }

            _referencedProjects = GetProject()
                .GetProjectReferences()
                .Select(
                    r => new
                    {
                        fullPath = Path.GetFullPath(Path.Combine(DirectoryPath, r.EvaluatedInclude)),
                        guid = Guid.Parse(r.GetMetadataValue("Project"))
                    })
                .Select(p => _codebase.LoadProject(p.fullPath, p.guid))
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
            var projectFullPath = FullPath.GetFileSystemPath();
            var projectUri = new Uri(projectFullPath);

            return new Uri(solution.FullPath).MakeRelativeUri(projectUri);
        }

        public void Save() => GetProject().Save();

        internal void AddSolution(Solution solution) => _solutions.Add(solution);

        private MsBuildProject GetProject()
        {
            if (_project == null)
            {
                throw new InvalidOperationException("The project could not be loaded.");
            }

            return _project;
        }
    }
}