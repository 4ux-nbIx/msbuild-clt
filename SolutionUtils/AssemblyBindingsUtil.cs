namespace MsBuild.Utils
{
    #region Namespace Imports

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Xml.Linq;

    #endregion


    internal class AssemblyBindingsUtil
    {
        private readonly ILogger _logger;

        public AssemblyBindingsUtil(ILogger logger) => _logger = logger;

        public bool Update(string appConfigPath, Dictionary<string, List<AssemblyReference>> assemblies)
        {
            var redirects = new List<AssemblyBindingRedirect>();

            foreach (var assembly in assemblies)
            {
                var assemblyVersions = assembly.Value.GroupBy(r => r.AssemblyName.FullName).ToList();

                if (assemblyVersions.Count == 1)
                {
                    continue;
                }
               
                var highestVersion = new Version(0, 0, 0, 0);

                foreach (var assemblyVersion in assemblyVersions)
                {
                    var version = assemblyVersion.First().AssemblyName.Version;

                    if (version > highestVersion)
                    {
                        highestVersion = version;
                    }
                }

                var assemblyName = assembly.Value.FirstOrDefault(r => r.ReferencingAssembly == null)?.AssemblyName;

                if (assemblyName == null)
                {
                    assemblyName = assembly.Value.First(a => a.AssemblyName.Version == highestVersion).AssemblyName;
                }

                redirects.Add(
                    new AssemblyBindingRedirect { AssemblyName = assemblyName, ToVersion = highestVersion, TargetVersion = assemblyName.Version });
            }

            if (redirects.Count <= 0)
            {
                return false;
            }

            try
            {
                Update(appConfigPath, redirects);

                return true;
            }
            catch (Exception e)
            {
                _logger.WriteError(e.ToString());
            }

            return false;
        }

        private static string GetPublicKeyToken(AssemblyName assembly)
        {
            var bytes = assembly.GetPublicKeyToken();

            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            var publicKeyToken = string.Empty;

            for (var i = 0; i < bytes.GetLength(0); i++)
            {
                publicKeyToken += string.Format("{0:x2}", bytes[i]);
            }

            return publicKeyToken;
        }

        private void Update(string path, List<AssemblyBindingRedirect> redirects)
        {
            const string xmlns = "urn:schemas-microsoft-com:asm.v1";

            XDocument document;

            if (File.Exists(path))
            {
                document = XDocument.Load(path);
            }
            else
            {
                document = new XDocument();
            }

            var bindingsElement = document.GetOrCreateElement("configuration")
                .GetOrCreateElement("runtime")
                .GetOrCreateElement("assemblyBinding", xmlns);

            var existingRedirects = bindingsElement.Descendants(XName.Get("assemblyIdentity", xmlns))
                .ToDictionary(e => e.Attribute("name")?.Value, e => e.Parent);

            foreach (var bindingRedirect in redirects)
            {
                _logger.WriteInfo($"Detected multiple versions of {bindingRedirect.AssemblyName}. Updating binding redirects...");

                XElement dependentAssemblyElement;

                if (!existingRedirects.TryGetValue(bindingRedirect.AssemblyName.Name, out dependentAssemblyElement))
                {
                    dependentAssemblyElement = new XElement(XName.Get("dependentAssembly", xmlns));
                    bindingsElement.Add(dependentAssemblyElement);

                    var assemblyIdentityElement = new XElement(XName.Get("assemblyIdentity", xmlns));
                    assemblyIdentityElement.SetAttributeValue("name",           bindingRedirect.AssemblyName.Name);

                    var publicKeyToken = GetPublicKeyToken(bindingRedirect.AssemblyName);
                    if (publicKeyToken != null)
                    {
                        assemblyIdentityElement.SetAttributeValue("publicKeyToken", publicKeyToken);
                    }

                    var cultureName = bindingRedirect.AssemblyName.CultureName;

                    if (string.IsNullOrEmpty(cultureName))
                    {
                        cultureName = "neutral";
                    }

                    assemblyIdentityElement.SetAttributeValue("culture", cultureName);

                    dependentAssemblyElement.Add(assemblyIdentityElement);
                }

                if (bindingRedirect.ToVersion > bindingRedirect.TargetVersion)
                {
                    _logger.WriteWarning($"Local {bindingRedirect.AssemblyName} version is lower than the version used by one of the dependencies.");
                }

                var bindingRedirectElement = dependentAssemblyElement.GetOrCreateElement("bindingRedirect", xmlns);
                bindingRedirectElement.SetAttributeValue("oldVersion", $"{bindingRedirect.FromVersion}-{bindingRedirect.ToVersion}");
                bindingRedirectElement.SetAttributeValue("newVersion", bindingRedirect.TargetVersion.ToString());
            }

            document.Save(path);
        }
    }
}