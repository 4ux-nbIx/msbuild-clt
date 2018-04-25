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

        public static IEnumerable<ProjectInSolution> GetMsBuildProjects(this SolutionFile solution) =>
            solution.ProjectsInOrder.Where(p => p.ProjectType != SolutionProjectType.SolutionFolder);

        public static void Update(
            this Solution solution,
            List<ProjectInSolution> removedProjects,
            List<ProjectInSolution> updatedProjects,
            List<CodebaseProject> newProjects,
            List<Tuple<string, string>> changedProjectGuids,
            Codebase codebase)
        {
            var path = solution.FullPath;

            var newLines = solution.Update(
                    File.ReadAllLines(path),
                    removedProjects,
                    updatedProjects,
                    newProjects,
                    changedProjectGuids,
                    codebase)
                .ToList();

            File.WriteAllLines(path, newLines);
        }

        public static void UpdateProjectGuid(this ProjectInSolution projectInSolution, string guid)
        {
            projectInSolution.GetType().GetProperty(nameof(ProjectInSolution.ProjectGuid))?.SetValue(projectInSolution, guid);
        }

        private static bool IsGlobalSectionLine(string line) => line.TrimStart().Equals("Global", StringComparison.Ordinal);

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

        private static string ReplaceProjectGuid(List<Tuple<string, string>> changedProjectGuids, string line)
        {
            foreach (var tuple in changedProjectGuids)
            {
                line = line.Replace(tuple.Item1, tuple.Item2);
            }

            return line;
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
            this Solution solution,
            string[] solutionFileLines,
            List<ProjectInSolution> removedProjects,
            List<ProjectInSolution> updatedProjects,
            List<CodebaseProject> newProjects,
            List<Tuple<string, string>> changedProjectGuids,
            Codebase codebase)
        {
            var removedProjectGuids = removedProjects.Select(p => p.ProjectGuid).ToList();
            var updatedProjectGuids = updatedProjects.Select(p => p.ProjectGuid).ToList();

            var enumerator = solutionFileLines.GetEnumerator();

            while (enumerator.MoveNext())
            {
                var line = (string)enumerator.Current;
                Debug.Assert(line != null);

                line = ReplaceProjectGuid(changedProjectGuids, line);

                if (IsProjectLine(line, removedProjectGuids))
                {
                    SkipProjectLines(enumerator);

                    continue;
                }

                if (IsProjectLine(line, updatedProjectGuids, out var projectGuid))
                {
                    var projectInSolution = updatedProjects.First(p => p.ProjectGuid == projectGuid);
                    var project = codebase.ProjectsByGuid[Guid.Parse(projectGuid)];
                    var projectRelativeUri = project.GetRelativePath(solution);

                    line = line.Replace(projectInSolution.RelativePath, projectRelativeUri.ToFileSystemPath())
                        .Replace(projectInSolution.ProjectName, project.Name);
                }

                if (newProjects != null && IsGlobalSectionLine(line))
                {
                    foreach (var newProject in newProjects)
                    {
                        var guid = newProject.Guid.ToString("B").ToUpperInvariant();
                        var relativePath = newProject.GetRelativePath(solution).ToFileSystemPath();
                        var typeGuid = newProject.SolutionProjectTypeGuid;

                        yield return $"Project(\"{{{typeGuid}}}\") = \"{newProject.Name}\", \"{relativePath}\", \"{{{guid}}}\"";
                        yield return "EndProject";
                    }
                }

                if (IsProjectConfigurationPlatformsSection(line) || IsNestedProjectsSection(line))
                {
                    yield return line;

                    foreach (var sectionLine in UpdateGlobalSection(enumerator, removedProjectGuids, changedProjectGuids))
                    {
                        yield return sectionLine;
                    }

                    continue;
                }

                yield return line;
            }
        }

        private static IEnumerable<string> UpdateGlobalSection(
            IEnumerator enumerator,
            List<string> removedProjectGuids,
            List<Tuple<string, string>> changedProjectGuids)
        {
            while (enumerator.MoveNext())
            {
                var line = (string)enumerator.Current;

                if (IsGlobalSectionProjectLine(line, removedProjectGuids))
                {
                    continue;
                }

                line = ReplaceProjectGuid(changedProjectGuids, line);

                yield return line;

                if (line?.Trim().Equals("EndGlobalSection", StringComparison.Ordinal) == true)
                {
                    yield break;
                }
            }
        }
    }
}