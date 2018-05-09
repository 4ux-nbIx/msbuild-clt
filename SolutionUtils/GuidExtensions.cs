namespace MsBuild.Utils
{
    #region Namespace Imports

    using System;

    #endregion


    internal static class GuidExtensions
    {
        public static string ToSolutionProjectGuid(this Guid guid) => guid.ToString("B").ToUpperInvariant();
    }
}