namespace MsBuild.Clt
{
    #region Namespace Imports

    using System.Xml.Linq;

    #endregion


    internal static class XElementExtensions
    {
        public static XElement GetOrCreateElement(this XContainer parentElement, string localName, string elementNamespace = null)
        {
            XName name;

            if (string.IsNullOrEmpty(elementNamespace))
            {
                name = XName.Get(localName);
            }
            else
            {
                name = XName.Get(localName, elementNamespace);
            }

            var element = parentElement.Element(name);

            if (element != null)
            {
                return element;
            }

            element = new XElement(name);
            parentElement.Add(element);

            return element;
        }
    }
}