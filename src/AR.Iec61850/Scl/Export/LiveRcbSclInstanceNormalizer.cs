using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace AR.Iec61850.Scl.Export;

/// <summary>
/// Normalizes a selected live MMS RCB instance into a one-instance SCL ReportControl.
/// Live discovery already exposes the concrete MMS object name (for example
/// A_BRCB_1201). In SCL, ReportControl@indexed defaults to true; leaving it omitted
/// causes an importer to append a second two-digit instance suffix and incorrectly
/// address A_BRCB_120101. A concrete live instance is therefore represented with
/// its exact name, indexed=false and RptEnabled max=1.
/// </summary>
public static class LiveRcbSclInstanceNormalizer
{
    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";

    public static LiveRcbSclInstanceNormalizationResult NormalizeFile(
        string sclPath,
        string exactRuntimeRcbName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sclPath);
        using var input = File.OpenRead(sclPath);
        var document = XDocument.Load(input, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var result = Normalize(document, exactRuntimeRcbName);

        using var output = File.Create(sclPath);
        using var writer = XmlWriter.Create(output, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = false
        });
        document.Save(writer);
        return result with { SclPath = Path.GetFullPath(sclPath) };
    }

    public static LiveRcbSclInstanceNormalizationResult Normalize(
        XDocument document,
        string exactRuntimeRcbName)
    {
        ArgumentNullException.ThrowIfNull(document);
        exactRuntimeRcbName = (exactRuntimeRcbName ?? string.Empty).Trim();
        if (exactRuntimeRcbName.Length == 0)
            throw new ArgumentException("The exact runtime RCB name is empty.", nameof(exactRuntimeRcbName));

        try
        {
            XmlConvert.VerifyNCName(exactRuntimeRcbName);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"Runtime RCB name '{exactRuntimeRcbName}' is not a valid SCL name.", ex);
        }

        var reportControls = document
            .Descendants()
            .Where(element => element.Name.LocalName == "ReportControl")
            .ToArray();
        if (reportControls.Length != 1)
        {
            throw new InvalidDataException(
                $"Selected-RCB CID normalization requires exactly one ReportControl; found {reportControls.Length}.");
        }

        var reportControl = reportControls[0];
        var previousName = ((string?)reportControl.Attribute("name") ?? string.Empty).Trim();
        reportControl.SetAttributeValue("name", exactRuntimeRcbName);
        reportControl.SetAttributeValue("indexed", "false");

        var rptEnabled = reportControl.Elements().FirstOrDefault(element => element.Name.LocalName == "RptEnabled");
        if (rptEnabled is null)
        {
            rptEnabled = new XElement(Scl + "RptEnabled");
            reportControl.Add(rptEnabled);
        }
        rptEnabled.SetAttributeValue("max", "1");
        foreach (var clientLn in rptEnabled.Elements().Where(element => element.Name.LocalName == "ClientLN").ToArray())
            clientLn.Remove();

        var services = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "ConfReportControl");
        services?.SetAttributeValue("max", "1");

        Validate(document, exactRuntimeRcbName);
        return new LiveRcbSclInstanceNormalizationResult
        {
            PreviousReportControlName = previousName,
            ExactRuntimeReportControlName = exactRuntimeRcbName,
            Indexed = false,
            InstanceCount = 1
        };
    }

    private static void Validate(XDocument document, string exactRuntimeRcbName)
    {
        var reportControl = document.Descendants().Single(element => element.Name.LocalName == "ReportControl");
        var actualName = ((string?)reportControl.Attribute("name") ?? string.Empty).Trim();
        if (!actualName.Equals(exactRuntimeRcbName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Selected-RCB CID changed runtime name '{exactRuntimeRcbName}' to '{actualName}'.");
        }

        if (!string.Equals((string?)reportControl.Attribute("indexed"), "false", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Concrete runtime RCB '{exactRuntimeRcbName}' must be exported with indexed=false.");
        }

        var rptEnabled = reportControl.Elements().SingleOrDefault(element => element.Name.LocalName == "RptEnabled");
        if (rptEnabled is null || !string.Equals((string?)rptEnabled.Attribute("max"), "1", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Concrete runtime RCB '{exactRuntimeRcbName}' must expose exactly one instance through RptEnabled max=1.");
        }

        if (rptEnabled.Elements().Any(element => element.Name.LocalName == "ClientLN"))
        {
            throw new InvalidDataException(
                $"Concrete runtime RCB '{exactRuntimeRcbName}' must not contain indexed ClientLN assignments.");
        }
    }
}

public sealed record LiveRcbSclInstanceNormalizationResult
{
    public string SclPath { get; init; } = string.Empty;
    public string PreviousReportControlName { get; init; } = string.Empty;
    public string ExactRuntimeReportControlName { get; init; } = string.Empty;
    public bool Indexed { get; init; }
    public int InstanceCount { get; init; }
}