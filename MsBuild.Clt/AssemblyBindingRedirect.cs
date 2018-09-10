namespace MsBuild.Clt
{
    #region Namespace Imports

    using System;
    using System.Reflection;

    #endregion


    internal class AssemblyBindingRedirect
    {
        public AssemblyName AssemblyName { get; set; }
        public Version FromVersion { get; set; } = new Version(0, 0, 0, 0);
        public Version TargetVersion { get; set; }
        public Version ToVersion { get; set; }
    }
}