namespace MsBuild.Clt
{
    #region Namespace Imports

    using System.Reflection;

    #endregion


    internal class AssemblyReference
    {
        public AssemblyName AssemblyName { get; set; }
        public AssemblyName ReferencingAssembly { get; set; }
    }
}