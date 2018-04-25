namespace MsBuild.Utils
{
    #region Namespace Imports

    using System;
    using System.Collections.Generic;

    using Microsoft.Build.Evaluation;

    #endregion


    internal static class ProjectExtensions
    {
        private const string _projectReferenceItemType = "ProjectReference";

        public static Guid GetProjectGuid(this Microsoft.Build.Evaluation.Project project)
        {
            var property = project.GetProperty("ProjectGuid");

            var guid = Guid.Parse(property.EvaluatedValue);
            return guid;
        }

        public static ICollection<ProjectItem> GetProjectReferences(this Microsoft.Build.Evaluation.Project project) =>
            project.GetItemsIgnoringCondition(_projectReferenceItemType);
    }
}