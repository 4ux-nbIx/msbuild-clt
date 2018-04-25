namespace MsBuild.Utils
{
    #region Namespace Imports

    using System;
    using System.IO;

    using Microsoft.Build.Construction;
    using Microsoft.Build.Evaluation;

    #endregion


    internal static class ProjectExtensions
    {
        public static string GetName(this Project project) => Path.GetFileNameWithoutExtension(project.FullPath.GetFileSystemName(out _));

        public static Guid GetProjectGuid(this Project project)
        {
            var property = project.GetProperty("ProjectGuid");

            var guid = Guid.Parse(property.EvaluatedValue);
            return guid;
        }

        public static Uri GetRelativePath(this Project project, SolutionFile solutionFile)
        {
            var projectFullPath = project.FullPath.GetFileSystemPath();
            var projectUri = new Uri(projectFullPath);

            return new Uri(solutionFile.GetFullPath()).MakeRelativeUri(projectUri);
        }

        public static string GetSolutionProjectTypeGuid(this Project project)
        {
            var extension = Path.GetExtension(project.FullPath)?.ToLowerInvariant();

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
}