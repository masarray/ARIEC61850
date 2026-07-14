using System.Xml;
using System.Xml.Linq;

namespace AR.Iec61850.Scl;

internal static class SclXmlDocumentLoader
{
    public static XDocument Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("SCL file path is empty.", nameof(filePath));
        if (!File.Exists(filePath))
            throw new FileNotFoundException("The selected SCL file does not exist.", filePath);

        using var stream = File.OpenRead(filePath);
        using var reader = XmlReader.Create(stream, CreateSettings());
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
    }

    public static XDocument Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new ArgumentException("SCL XML is empty.", nameof(xml));

        using var textReader = new StringReader(xml);
        using var reader = XmlReader.Create(textReader, CreateSettings());
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
    }

    private static XmlReaderSettings CreateSettings()
        => new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = false,
            IgnoreProcessingInstructions = false,
            IgnoreWhitespace = false,
            CloseInput = true
        };
}
