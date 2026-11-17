using System.Xml;

namespace GoymenERP.Modules.Documents.Domain;

/// <summary>Structural vault inspection only. Does not validate a tax document, verify its signature,
/// read business amounts, execute transforms or fetch schemas. The malware scanner still runs.</summary>
internal static class DocumentUblInvoiceInspection
{
    private const string InvoiceNamespace = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private const int MaximumDepth = 64;

    internal static bool IsPassiveInvoice(ReadOnlySpan<byte> content)
    {
        try
        {
            using var stream = new MemoryStream(content.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = content.Length, MaxCharactersFromEntities = 1024,
                ConformanceLevel = ConformanceLevel.Document,
            });
            var rootSeen = false;
            while (reader.Read())
            {
                if (reader.Depth > MaximumDepth || reader.NodeType is XmlNodeType.ProcessingInstruction
                    or XmlNodeType.DocumentType or XmlNodeType.EntityReference) return false;
                if (reader.NodeType != XmlNodeType.Element || reader.Depth != 0) continue;
                if (rootSeen || reader.LocalName != "Invoice" || reader.NamespaceURI != InvoiceNamespace) return false;
                rootSeen = true;
            }
            return rootSeen;
        }
        catch (XmlException) { return false; }
        catch (ArgumentException) { return false; }
    }
}
