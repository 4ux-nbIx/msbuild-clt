namespace MsBuild.Clt
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

        internal Project(Codebase codebase, MsBuildProject project, Guid guid, ILogger logger)
            : this(codebase, project.FullPath.GetFileSystemPath(), project.GetProjectGuid() ?? guid, logger) =>
            _project = project;

        internal Project(Codebase codebase, string absolutePath, Guid guid, ILogger logger)
        {
            _codebase = codebase;
            _logger = logger;

            FullPath = absolutePath.GetFileSystemPath();
            Guid = guid;
            Name = Path.GetFileNameWithoutExtension(FullPath);
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

        public void FixProjectReferences()
        {
            var brokenReferences = GetProject()
                .GetProjectReferences()
                .Select(r => GetProjectReferencePathAndGuid(r))
                .Where(p => !_codebase.ProjectsByFileName.ContainsKey(p.fullPath.ToLowerInvariant()))
                .ToList();

            foreach (var brokenReference in brokenReferences)
            {
                var fileName = Path.GetFileName(brokenReference.fullPath);

                var projects = _codebase.FindProjectsByFileName(fileName);

                if (projects.Count == 1)
                {
                    FixProjectReference(brokenReference.item, projects[0]);
                }

                if (_codebase.ProjectsByGuid.TryGetValue(brokenReference.guid, out var project))
                {
                    FixProjectReference(brokenReference.item, project);

                    continue;
                }

                _logger.WriteWarning($"Failed to resolve project {Name} reference.");
            }

            if (!IsDirty)
            {
                return;
            }

            GetProject().Save();
            _referencedProjects = null;
        }

        public IEnumerable<Project> GetAllReferencedProjects(bool includeUnsupported = false)
        {
            if (includeUnsupported && IsNotSupported)
            {
                return Enumerable.Empty<Project>();
            }

            var projects = GetReferencedProjects().Where(p => includeUnsupported || !p.IsNotSupported).ToList();

            return projects.Concat(projects.SelectMany(p => p.GetAllReferencedProjects(includeUnsupported))).Distinct();
        }

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
                .Select(r => GetProjectReferencePathAndGuid(r))
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

        public string GetRelativePath(Solution solution) => FullPath.ToRelativePath(solution.FullPath);

        public void Save() => GetProject().Save();

        public override string ToString() => $"{nameof(FullPath)}: {FullPath}, {nameof(Guid)}: {Guid}";

        internal void AddSolution(Solution solution) => _solutions.Add(solution);

        private void FixProjectReference(ProjectItem projectItem, Project project)
        {
            projectItem.UnevaluatedInclude = project.FullPath.ToRelativePath(FullPath);
            projectItem.SetMetadataValue("Project", project.Guid.ToString("B"));
        }

        private MsBuildProject GetProject()
        {
            if (_project == null)
            {
                throw new InvalidOperationException("The project could not be loaded.");
            }

            return _project;
        }

        private (string fullPath, Guid guid, ProjectItem item) GetProjectReferencePathAndGuid(ProjectItem r)
        {
            var guid = Guid.Empty;
            var metadataGuidValue = r.GetMetadataValue("Project");

            if (!string.IsNullOrWhiteSpace(metadataGuidValue))
            {
                guid = Guid.Parse(metadataGuidValue);
            }

            return (Path.GetFullPath(Path.Combine(DirectoryPath, r.EvaluatedInclude)), guid, r);
        }
    }
}