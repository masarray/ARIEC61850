using AR.Iec61850.Binding;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Binding;

public sealed class Iec61850ValueBindingEngineTests
{
    [Fact]
    public void SchemaBuilder_Creates_Dpc_Control_Object_Schema_Without_Raw_Aggregate_Guess()
    {
        var schema = Iec61850DataObjectSchemaBuilder.FromLiveDataObject(CreateSwitchPositionDataObject());

        Assert.Equal("DPC", schema.Cdc);
        Assert.DoesNotContain(schema.Attributes, x => x.Name.Equals("Pos", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(schema.Attributes, x => x.Name.Equals("stVal", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(schema.Attributes, x => x.Name.Equals("q", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(schema.Attributes, x => x.Name.Equals("t", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(schema.Attributes, x => x.Name.Equals("Oper", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(schema.Attributes, x => x.Name.Equals("SBOw", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(schema.Attributes, x => x.Name.Equals("ctlModel", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Quality_BitString_Decodes_To_Named_Flags()
    {
        var schema = Iec61850DataObjectSchemaBuilder.FromLiveDataObject(CreateSwitchPositionDataObject());
        var q = schema.Attributes.First(x => x.Name.Equals("q", StringComparison.OrdinalIgnoreCase));

        var result = Iec61850ValueBindingEngine.Bind(q, MmsDataValue.BitString(3, new byte[] { 0x00, 0x00 }));

        Assert.Equal("good", result.Root.Value);
        Assert.Equal("good", result.Root.Quality);
        Assert.Contains(result.Root.Children, x => x.Name.Equals("Validity", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Root.Children, x => x.Name.Equals("Quality Details", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Quality_Decoder_Does_Not_Treat_A_Short_BitString_As_IecQuality()
    {
        var decoded = Iec61850QualityDecoder.Decode(MmsDataValue.BitString(6, new byte[] { 0x04 }));

        Assert.False(decoded.IsDecoded);
    }

    [Fact]
    public void Timestamp_Decodes_To_Readable_Time_And_Time_Quality_Rows()
    {
        var schema = Iec61850DataObjectSchemaBuilder.FromLiveDataObject(CreateSwitchPositionDataObject());
        var t = schema.Attributes.First(x => x.Name.Equals("t", StringComparison.OrdinalIgnoreCase));
        var utc = new Iec61850UtcTime(new DateTimeOffset(2026, 6, 13, 14, 58, 23, TimeSpan.Zero), 0x20);

        var result = Iec61850ValueBindingEngine.Bind(t, MmsDataValue.UtcTime(utc));

        Assert.Contains("2026", result.Root.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Root.Children, x => x.Name.Equals("ClockNotSynchronized", StringComparison.OrdinalIgnoreCase) && x.Value == "true");
    }

    [Fact]
    public void Control_Operation_Structure_Uses_Named_Children_Instead_Of_Positional_Index_Guesses()
    {
        var schema = Iec61850DataObjectSchemaBuilder.FromLiveDataObject(CreateSwitchPositionDataObject());
        var oper = schema.Attributes.First(x => x.Name.Equals("Oper", StringComparison.OrdinalIgnoreCase));
        var operation = MmsDataValue.Structure(new[]
        {
            MmsDataValue.Integer(2),
            MmsDataValue.Structure(new[] { MmsDataValue.Integer(8), MmsDataValue.OctetString(new byte[] { 0x13, 0xD5, 0xC0, 0x07 }) }),
            MmsDataValue.Unsigned(7),
            MmsDataValue.UtcTime(new Iec61850UtcTime(new DateTimeOffset(2026, 6, 13, 14, 58, 23, TimeSpan.Zero), 0x00)),
            MmsDataValue.Boolean(false),
            MmsDataValue.BitString(6, new byte[] { 0xC0 })
        });

        var result = Iec61850ValueBindingEngine.Bind(oper, operation);

        Assert.Empty(result.Diagnostics.Where(x => x.StartsWith("LOW_CONFIDENCE_RAW_STRUCTURE", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(new[] { "ctlVal", "origin", "ctlNum", "T", "Test", "Check" }, result.Root.Children.Select(x => x.Name).ToArray());
        Assert.Equal("on", result.Root.Children[0].Value);
        Assert.Equal("process", result.Root.Children[1].Children[0].Value);
        Assert.Equal("InterlockCheck, Synchrocheck", result.Root.Children[5].Value);
    }

    [Fact]
    public void Control_Model_Enum_Decodes_To_Engineering_Label()
    {
        var schema = Iec61850DataObjectSchemaBuilder.FromLiveDataObject(CreateSwitchPositionDataObject());
        var ctlModel = schema.Attributes.First(x => x.Name.Equals("ctlModel", StringComparison.OrdinalIgnoreCase));

        var result = Iec61850ValueBindingEngine.Bind(ctlModel, MmsDataValue.Integer(4));

        Assert.Equal("sbo-with-enhanced-security", result.Root.Value);
    }

    private static LiveIedDataObjectModel CreateSwitchPositionDataObject()
        => new()
        {
            Reference = "IEDLD0/Q0CSWI1.Pos",
            Name = "Pos",
            InferredCdc = "DPC",
            CdcConfidence = 0.94,
            ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.High,
            Attributes = new[]
            {
                Attribute("IEDLD0/Q0CSWI1.Pos", "Pos", "ST", "Struct"),
                Attribute("IEDLD0/Q0CSWI1.Pos.stVal", "stVal", "ST", "Dbpos"),
                Attribute("IEDLD0/Q0CSWI1.Pos.q", "q", "ST", "Quality"),
                Attribute("IEDLD0/Q0CSWI1.Pos.t", "t", "ST", "Timestamp"),
                Attribute("IEDLD0/Q0CSWI1.Pos.stSeld", "stSeld", "ST", "BOOLEAN"),
                Attribute("IEDLD0/Q0CSWI1.Pos.SBOw", "SBOw", "CO", "Struct"),
                Attribute("IEDLD0/Q0CSWI1.Pos.Oper", "Oper", "CO", "Struct"),
                Attribute("IEDLD0/Q0CSWI1.Pos.Cancel", "Cancel", "CO", "Struct"),
                Attribute("IEDLD0/Q0CSWI1.Pos.ctlModel", "ctlModel", "CF", "Enum")
            }
        };

    private static LiveIedDataAttributeModel Attribute(string reference, string path, string fc, string type)
        => new()
        {
            ObjectReference = reference,
            AttributePath = path,
            FunctionalConstraint = fc,
            MmsReference = reference.Replace('/', '$').Replace('.', '$'),
            MmsItemName = path,
            Source = "unit-test",
            SclBType = type,
            MmsType = type == "Struct" ? "Structure" : type,
            MmsTypeSignature = type,
            TypeDiscoveryStatus = "Exact",
            TypeSource = "UnitTestSchema",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact,
            FunctionalConstraintConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };
}
