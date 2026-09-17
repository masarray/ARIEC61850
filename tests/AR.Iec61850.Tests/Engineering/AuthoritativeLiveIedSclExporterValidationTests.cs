using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using AR.Iec61850.Scl.Export;

namespace AR.Iec61850.Tests.Engineering;

public sealed class AuthoritativeLiveIedSclExporterValidationTests
{
    private static readonly XNamespace Scl = "http://www.iec.ch/61850/2003/SCL";

    [Fact]
    public void ValidateExportGraph_AcceptsResolvedDataSetWithValidFcda()
    {
        var document = Document(
            new XElement(Scl + "DataSet",
                new XAttribute("name", "Analog"),
                new XElement(Scl + "FCDA",
                    new XAttribute("ldInst", "LD0"),
                    new XAttribute("lnClass", "MMXU"),
                    new XAttribute("lnInst", "1"),
                    new XAttribute("doName", "A"),
                    new XAttribute("daName", "phsA.cVal.mag.f"),
                    new XAttribute("fc", "MX"))),
            new XElement(Scl + "ReportControl",
                new XAttribute("name", "BRCB01"),
                new XAttribute("datSet", "Analog")));

        Validate(document);
    }

    [Fact]
    public void ValidateExportGraph_RejectsUnresolvedReportControlDataSet()
    {
        var document = Document(
            new XElement(Scl + "ReportControl",
                new XAttribute("name", "BRCB01"),
                new XAttribute("datSet", "Missing")));

        var error = Assert.Throws<InvalidDataException>(() => Validate(document));

        Assert.Contains("references DataSet 'Missing'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateExportGraph_RejectsEmptyDataSetBoundToReportControl()
    {
        var document = Document(
            new XElement(Scl + "DataSet", new XAttribute("name", "Digital")),
            new XElement(Scl + "ReportControl",
                new XAttribute("name", "URCB01"),
                new XAttribute("datSet", "Digital")));

        var error = Assert.Throws<InvalidDataException>(() => Validate(document));

        Assert.Contains("contains no valid FCDA members", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateExportGraph_RejectsFcdaMissingRequiredIdentity()
    {
        var document = Document(
            new XElement(Scl + "DataSet",
                new XAttribute("name", "Digital"),
                new XElement(Scl + "FCDA",
                    new XAttribute("ldInst", "LD0"),
                    new XAttribute("lnClass", "GGIO"),
                    new XAttribute("doName", "Ind1"))),
            new XElement(Scl + "ReportControl",
                new XAttribute("name", "URCB01"),
                new XAttribute("datSet", "Digital")));

        var error = Assert.Throws<InvalidDataException>(() => Validate(document));

        Assert.Contains("without required 'fc' identity", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateExportGraph_AllowsReportControlWithoutDataSetBinding()
    {
        var document = Document(
            new XElement(Scl + "ReportControl",
                new XAttribute("name", "URCB01")));

        Validate(document);
    }

    private static XDocument Document(params XElement[] logicalNodeChildren)
        => new(
            new XElement(Scl + "SCL",
                new XElement(Scl + "IED",
                    new XAttribute("name", "IED_A"),
                    new XElement(Scl + "AccessPoint",
                        new XAttribute("name", "AP1"),
                        new XElement(Scl + "Server",
                            new XElement(Scl + "LDevice",
                                new XAttribute("inst", "LD0"),
                                new XElement(Scl + "LN0",
                                    new XAttribute("lnClass", "LLN0"),
                                    new XAttribute("inst", string.Empty),
                                    logicalNodeChildren))))))));

    private static void Validate(XDocument document)
    {
        var method = typeof(AuthoritativeLiveIedSclExporter).GetMethod(
            "ValidateExportGraph",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(
                typeof(AuthoritativeLiveIedSclExporter).FullName,
                "ValidateExportGraph");

        try
        {
            method.Invoke(null, [document]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
