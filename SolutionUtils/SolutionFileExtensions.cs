namespace MsBuild.Utils
{
    #region Namespace Imports

    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Reflection;

    using JetBrains.Annotations;

    using Microsoft.Build.Construction;

    #endregion


    internal static class SolutionFileExtensions
    {
        public static string GetFullPath(this SolutionFile solution) =>
            solution.GetType().GetProperty("FullPath", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(solution) as string;

        public static void Update(
            this SolutionFile solution,
            List<ProjectInSolution> removedProjects,
            List<ProjectInSolution> movedProjects,
            Codebase codebase)
        {
            var path = solution.GetFullPath();

            var newLines = solution.Update(File.ReadAllLines(path), removedProjects, movedProjects, codebase).ToList();

            File.WriteAllLines(path, newLines);
        }

        private static string GetFileSystemName(string projectFullPath, out string directoryName)
        {
            directoryName = Path.GetDirectoryName(projectFullPath);

            if (directoryName == null)
            {
                directoryName = projectFullPath;
                return null;
            }

            var name = Path.GetFileName(projectFullPath);
            Debug.Assert(name != null, nameof(name) + " != null");

            var entry = Directory.GetFileSystemEntries(directoryName, name).First();
            return Path.GetFileName(entry);
        }

        private static string GetFileSystemPath(string projectFullPath)
        {
            var result = GetFileSystemName(projectFullPath, out var parent);

            while (true)
            {
                var name = GetFileSystemName(parent, out parent);

                if (name == null)
                {
                    return parent + result;
                }

                result = name + Path.DirectorySeparatorChar + result;
            }
        }

        private static bool IsGlobalSectionProjectLine(string line, List<string> projectGuids)
        {
            line = line.TrimStart();

            foreach (var guid in projectGuids)
            {
                if (line.StartsWith(guid, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNestedProjectsSection(string solutionLine) =>
            solutionLine.TrimStart().StartsWith("GlobalSection(NestedProjects)", StringComparison.Ordinal);

        private static bool IsProjectConfigurationPlatformsSection(string solutionLine) =>
            solutionLine.TrimStart().StartsWith("GlobalSection(ProjectConfigurationPlatforms)", StringComparison.Ordinal);

        private static bool IsProjectLine([NotNull] string line) => line.TrimStart().StartsWith("Project(", StringComparison.Ordinal);

        private static bool IsProjectLine(string projectLine, List<string> projectGuids) => IsProjectLine(projectLine, projectGuids, out _);

        private static bool IsProjectLine(string projectLine, List<string> projectGuids, out string projectGuid)
        {
            projectGuid = null;

            if (!IsProjectLine(projectLine))
            {
                return false;
            }

            projectLine = projectLine.TrimEnd();

            foreach (var guid in projectGuids)
            {
                if (projectLine.EndsWith($"\"{guid}\"", StringComparison.OrdinalIgnoreCase))
                {
                    projectGuid = guid;

                    return true;
                }
            }

            return false;
        }

        private static void SkipProjectLines(IEnumerator enumerator)
        {
            while (enumerator.MoveNext())
            {
                var line = (string)enumerator.Current;

                if (line?.Trim().Equals("EndProject", StringComparison.Ordinal) == true)
                {
                    return;
                }
            }
        }

        private static IEnumerable<string> Update(
            this SolutionFile solution,
            string[] solutionFileLines,
            List<ProjectInSolution> removedProjects,
            List<ProjectInSolution> updatedProjects,
            Codebase codebase)
        {
            var solutionUri = new Uri(solution.GetFullPath());

            var removedProjectGuids = removedProjects.Select(p => p.ProjectGuid).ToList();
            var updatedProjectGuids = updatedProjects.Select(p => p.ProjectGuid).ToList();

            var enumerator = solutionFileLines.GetEnumerator();

            while (enumerator.MoveNext())
            {
                var line = (string)enumerator.Current;
                Debug.Assert(line != null);

                if (IsProjectLine(line, removedProjectGuids))
                {
                    SkipProjectLines(enumerator);

                    continue;
                }

                if (IsProjectLine(line, updatedProjectGuids, out var projectGuid))
                {
                    var projectInSolution = updatedProjects.First(p => p.ProjectGuid == projectGuid);
                    var project = codebase.ProjectsByGuid[Guid.Parse(projectGuid)];
                    var projectFullPath = GetFileSystemPath(project.FullPath);
                    var projectUri = new Uri(projectFullPath);

                    var projectRelativeUri = solutionUri.MakeRelativeUri(projectUri);
                    var newProjectName = Path.GetFileNameWithoutExtension(projectFullPath);

                    line = line.Replace(
                            projectInSolution.RelativePath,
                            projectRelativeUri.ToString().Replace('/', Path.DirectorySeparatorChar))
                        .Replace(projectInSolution.ProjectName, newProjectName);
                }

                if (IsProjectConfigurationPlatformsSection(line) || IsNestedProjectsSection(line))
                {
                    yield return line;

                    foreach (var sectionLine in UpdateGlobalSection(enumerator, removedProjectGuids))
                    {
                        yield return sectionLine;
                    }

                    continue;
                }

                yield return line;
            }
        }

        private static IEnumerable<string> UpdateGlobalSection(IEnumerator enumerator, List<string> removedProjectGuids)
        {
            while (enumerator.MoveNext())
            {
                var line = (string)enumerator.Current;

                if (IsGlobalSectionProjectLine(line, removedProjectGuids))
                {
                    continue;
                }

                yield return line;

                if (line?.Trim().Equals("EndGlobalSection", StringComparison.Ordinal) == true)
                {
                    yield break;
                }
            }
        }
    }
}