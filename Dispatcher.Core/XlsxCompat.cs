using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Dispatcher.Core;

/// <summary>
/// ClosedXML writes SpreadsheetML with an "x:" namespace prefix on every element and a UTF-8 BOM.
/// Excel accepts that, but some lightweight viewers (e.g. VS Code spreadsheet previews) show the
/// sheets as empty. This rewrites the XML parts the way Excel itself does: the main namespace as the
/// default (unprefixed) namespace and no BOM. The content is unchanged.
/// </summary>
internal static class XlsxCompat
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static void Normalize(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("xl/") && e.FullName.EndsWith(".xml")).ToList())
        {
            XDocument doc;
            using (var read = entry.Open())
                doc = XDocument.Load(read, LoadOptions.PreserveWhitespace);
            var root = doc.Root!;
            if (root.Name.Namespace != Main)
                continue;

            // Drop the "x" prefix declaration and declare the main namespace as the default instead.
            foreach (var element in root.DescendantsAndSelf())
                element.Attributes()
                    .Where(a => a.IsNamespaceDeclaration && a.Value == Main.NamespaceName)
                    .Remove();
            root.SetAttributeValue("xmlns", Main.NamespaceName);

            var name = entry.FullName;
            entry.Delete();
            var replacement = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var write = replacement.Open();
            using var writer = XmlWriter.Create(write, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            });
            doc.Save(writer);
        }
    }
}
