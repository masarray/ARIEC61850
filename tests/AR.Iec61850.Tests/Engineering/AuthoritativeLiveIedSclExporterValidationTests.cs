using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using AR.Iec61850.Discovery;
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

    [Fact]
    public void ApplyReportControlConfiguration_CollapsesCompatibleRuntimeSiblingsToIndexedLogicalControl()
    {
        var controls = new[]
        {
            RuntimeControl("Buffer01", buffered: true, reportId: "RID_Buffer01", dataSet: "IED_ALD0/LLN0$Analog"),
            RuntimeControl("Buffer02", buffered: true, reportId: "RID_Buffer02", dataSet: "IED_ALD0/LLN0$Analog")
        };

        var result = ApplyReportProjection(controls);
        var exported = result.Descendants(Scl + "ReportControl").Single();

        Assert.Equal("Buffer", (string?)exported.Attribute("name"));
        Assert.Equal("true", (string?)exported.Attribute("indexed"));
        Assert.Equal("RID_Buffer", (string?)exported.Attribute("rptID"));
        Assert.Equal("2", (string?)exported.Element(Scl + "RptEnabled")?.Attribute("max"));
        Assert.DoesNotContain(result.Descendants(Scl + "ReportControl"), element =>
            string.Equals((string?)element.Attribute("name"), "Buffer01", StringComparison.OrdinalIgnoreCase) ||
            string.Equals((string?)element.Attribute("name"), "Buffer02", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("1", (string?)result.Descendants(Scl + "ConfReportControl").Single().Attribute("max"));
    }

    [Fact]
    public void ApplyReportControlConfiguration_LeavesSingletonConcreteAndNonIndexed()
    {
        var result = ApplyReportProjection(new[]
        {
            RuntimeControl("Buffer01", buffered: true, reportId: "RID_Buffer01", dataSet: "IED_ALD0/LLN0$Analog")
        });
        var exported = result.Descendants(Scl + "ReportControl").Single();

        Assert.Equal("Buffer01", (string?)exported.Attribute("name"));
        Assert.Equal("false", (string?)exported.Attribute("indexed"));
        Assert.Equal("1", (string?)exported.Element(Scl + "RptEnabled")?.Attribute("max"));
    }

    [Fact]
    public void ApplyReportControlConfiguration_DoesNotCollapseNonContiguousInstances()
    {
        var result = ApplyReportProjection(new[]
        {
            RuntimeControl("Buffer01", buffered: true, reportId: "RID_Buffer01", dataSet: "IED_ALD0/LLN0$Analog"),
            RuntimeControl("Buffer03", buffered: true, reportId: "RID_Buffer03", dataSet: "IED_ALD0/LLN0$Analog")
        });
        var exported = result.Descendants(Scl + "ReportControl").ToArray();

        Assert.Equal(2, exported.Length);
        Assert.All(exported, element => Assert.Equal("false", (string?)element.Attribute("indexed")));
        Assert.All(exported, element => Assert.Equal("1", (string?)element.Element(Scl + "RptEnabled")?.Attribute("max")));
    }

    [Fact]
    public void ApplyReportControlConfiguration_DoesNotCollapseStaticConfigurationConflict()
    {
        var result = ApplyReportProjection(new[]
        {
            RuntimeControl("Unbuffer01", buffered: false, reportId: "RID_Unbuffer01", dataSet: "IED_ALD0/LLN0$Analog"),
            RuntimeControl("Unbuffer02", buffered: false, reportId: "RID_Unbuffer02", dataSet: "IED_ALD0/LLN0$Digital")
        });
        var exported = result.Descendants(Scl + "ReportControl").ToArray();

        Assert.Equal(2, exported.Length);
        Assert.Contains(exported, element => string.Equals((string?)element.Attribute("name"), "Unbuffer01", StringComparison.Ordinal));
        Assert.Contains(exported, element => string.Equals((string?)element.Attribute("name"), "Unbuffer02", StringComparison.Ordinal));
        Assert.All(exported, element => Assert.Equal("false", (string?)element.Attribute("indexed")));
    }

    [Fact]
    public void ApplyReportControlConfiguration_ProjectsThirtyFourRuntimeInstancesToThirtyTwoLogicalControls()
    {
        var controls = Enumerable.Range(1, 30)
            .Select(index => RuntimeControl($"Standalone_{index}_X", buffered: false, reportId: $"RID_{index}_X", dataSet: string.Empty))
            .Concat(new[]
            {
                RuntimeControl("Buffer01", buffered: true, reportId: "RID_Buffer01", dataSet: "IED_ALD0/LLN0$Analog"),
                RuntimeControl("Buffer02", buffered: true, reportId: "RID_Buffer02", dataSet: "IED_ALD0/LLN0$Analog"),
                RuntimeControl("Unbuffer01", buffered: false, reportId: "RID_Unbuffer01", dataSet: "IED_ALD0/LLN0$Digital"),
                RuntimeControl("Unbuffer02", buffered: false, reportId: "RID_Unbuffer02", dataSet: "IED_ALD0/LLN0$Digital")
            })
            .ToArray();

        var result = ApplyReportProjection(controls);
        var exported = result.Descendants(Scl + "ReportControl").ToArray();

        Assert.Equal(34, controls.Length);
        Assert.Equal(32, exported.Length);
        Assert.Equal("32", (string?)result.Descendants(Scl + "ConfReportControl").Single().Attribute("max"));

        var buffer = exported.Single(element => string.Equals((string?)element.Attribute("name"), "Buffer", StringComparison.Ordinal));
        Assert.Equal("true", (string?)buffer.Attribute("indexed"));
        Assert.Equal("2", (string?)buffer.Element(Scl + "RptEnabled")?.Attribute("max"));

        var unbuffer = exported.Single(element => string.Equals((string?)element.Attribute("name"), "Unbuffer", StringComparison.Ordinal));
        Assert.Equal("true", (string?)unbuffer.Attribute("indexed"));
        Assert.Equal("2", (string?)unbuffer.Element(Scl + "RptEnabled")?.Attribute("max"));
    }

    private static XDocument ApplyReportProjection(IReadOnlyList<LiveIedReportControlModel> controls)
    {
        var source = ReportDocument(controls);
        var model = new LiveIedModelDiscoveryDocument
        {
            IedName = "IED_A",
            ReportControls = controls
        };
        return AuthoritativeLiveIedSclExporter.ApplyReportControlConfiguration(
            source,
            model,
            SclSchemaProfiles.Get(SclSchemaProfile.Edition2V31));
    }

    private static LiveIedReportControlModel RuntimeControl(
        string name,
        bool buffered,
        string reportId,
        string dataSet)
        => new()
        {
            Reference = $"IED_ALD0/LLN0${(buffered ? "BR" : "RP")}${name}",
            Domain = "IED_ALD0",
            LogicalNode = "LLN0",
            Name = name,
            Buffered = buffered,
            DataSetReference = dataSet,
            ReportId = reportId,
            ConfRev = "1",
            TriggerOptions = "dchg,qchg,gi",
            OptionalFields = "seqnum,timestamp,dataset,dataref",
            BufferTimeMs = buffered ? "10" : "0",
            IntegrityPeriodMs = "1000"
        };

    private static XDocument ReportDocument(IReadOnlyList<LiveIedReportControlModel> controls)
        => new(
            new XElement(Scl + "SCL",
                new XElement(Scl + "IED",
                    new XAttribute("name", "IED_A"),
                    new XElement(Scl + "Services",
                        new XElement(Scl + "ConfReportControl",
                            new XAttribute("max", controls.Count))),
                    new XElement(Scl + "AccessPoint",
                        new XAttribute("name", "AP1"),
                        new XElement(Scl + "Server",
                            new XElement(Scl + "LDevice",
                                new XAttribute("inst", "LD0"),
                                new XElement(Scl + "LN0",
                                    new XAttribute("lnClass", "LLN0"),
                                    controls.Select(control =>
                                        new XElement(Scl + "ReportControl",
                                            new XAttribute("name", control.Name),
                                            new XAttribute("buffered", control.Buffered ? "true" : "false"),
                                            new XAttribute("confRev", "1"),
                                            new XElement(Scl + "TrgOps"),
                                            new XElement(Scl + "OptFields"),
                                            new XElement(Scl + "RptEnabled", new XAttribute("max", "1")))))))))));

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
                                    logicalNodeChildren)))))));

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
