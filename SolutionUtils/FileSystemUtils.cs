namespace MsBuild.Utils
{
    #region Namespace Imports

    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;

    #endregion


    internal static class FileSystemUtils
    {
        public static string GetFileSystemName(this string projectFullPath, out string directoryName)
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

        public static string GetFileSystemPath(this string projectFullPath)
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

        public static string ToFileSystemPath(this Uri uri) => uri.ToString().Replace('/', Path.DirectorySeparatorChar);

        public static string ToRelativePath(this string path, string toPath)
        {
            var projectUri = new Uri(path);

            return new Uri(toPath).MakeRelativeUri(projectUri).ToFileSystemPath();
        }
    }
}