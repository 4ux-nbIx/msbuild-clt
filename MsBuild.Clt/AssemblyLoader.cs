namespace MsBuild.Clt
{
    #region Namespace Imports

    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;

    #endregion


    internal class AssemblyLoader
    {
        private readonly ILogger _logger;
        private readonly Dictionary<string, AssemblyName> _processedAssemblies = new Dictionary<string, AssemblyName>();

        public AssemblyLoader(ILogger logger) => _logger = logger;

        public Dictionary<string, List<AssemblyReference>> AssemblyNames { get; } = new Dictionary<string, List<AssemblyReference>>();

        public void Load(string folder)
        {
            AssemblyNames.Clear();
            _processedAssemblies.Clear();

            if (!Directory.Exists(folder))
            {
                _logger.WriteWarning($"Unable to check assembly references. Directory '{folder}' not found.");
                return;
            }

            var assemblyFiles = Directory.GetFiles(folder, "*.dll").ToList();

            foreach (var assemblyFile in assemblyFiles)
            {
                try
                {
                    var assembly = Assembly.LoadFile(assemblyFile);

                    ProcessAssembly(assembly);
                }
                catch
                {
                    _logger.WriteWarning($"Failed to load {assemblyFile}");
                }
            }
        }

        private void AddAssembly(AssemblyName assemblyName, AssemblyName referencingAssembly)
        {
            List<AssemblyReference> assemblyReferences;

            if (!AssemblyNames.TryGetValue(assemblyName.Name, out assemblyReferences))
            {
                assemblyReferences = new List<AssemblyReference>();
                AssemblyNames.Add(assemblyName.Name, assemblyReferences);
            }

            assemblyReferences.Add(new AssemblyReference { AssemblyName = assemblyName, ReferencingAssembly = referencingAssembly });

            if (!_processedAssemblies.ContainsKey(assemblyName.FullName))
            {
                _processedAssemblies.Add(assemblyName.FullName, assemblyName);
            }
        }

        private bool IsAlreadyProcessed(AssemblyName assemblyName) => _processedAssemblies.ContainsKey(assemblyName.FullName);

        private void ProcessAssembly(Assembly assembly)
        {
            var assemblyName = assembly.GetName();

            AddAssembly(assemblyName, null);

            var referencedAssemblies = assembly.GetReferencedAssemblies();

            foreach (var referencedAssemblyName in referencedAssemblies)
            {
                AddAssembly(referencedAssemblyName, assemblyName);

                try
                {
                    if (IsAlreadyProcessed(referencedAssemblyName))
                    {
                        continue;
                    }

                    var referencedAssembly = Assembly.Load(referencedAssemblyName);

                    ProcessAssembly(referencedAssembly);
                }
                catch
                {
                    _logger.WriteWarning($"Failed to load {referencedAssemblyName.FullName}.");
                }
            }
        }
    }
}