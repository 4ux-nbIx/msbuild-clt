namespace MsBuild.Clt
{
    #region Namespace Imports

    using System.IO;

    using Microsoft.Build.Evaluation;

    #endregion


    public static class ProjectItemExtensions
    {
        public static string GetFullPath(this ProjectItem projectItem)
        {
            var fullPath = Path.Combine(projectItem.Project.DirectoryPath, projectItem.EvaluatedInclude);
            return Path.GetFullPath(fullPath);
        }
    }
}