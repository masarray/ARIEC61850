using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public class MmsReportDiscoveryMapperTests
{
    [Fact]
    public void BuildInventoryFindsDataSetsAndReportControls()
    {
        var snapshot = new MmsDiscoverySnapshot
        {
            DomainVariables = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["OCR7SR12PROT"] =
                [
                    "LLN0$BR$brcbA01$RptID",
                    "LLN0$BR$brcbA01$DatSet",
                    "LLN0$RP$urcbA01$RptID"
                ]
            },
            DomainVariableLists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["OCR7SR12PROT"] = ["LLN0$DataSet"]
            }
        };

        var inventory = MmsReportDiscoveryMapper.BuildInventory(snapshot);

        Assert.Single(inventory.DataSets);
        Assert.Equal("OCR7SR12PROT/LLN0.DataSet", inventory.DataSets[0].Reference);
        Assert.Equal(2, inventory.ReportControls.Count);
        Assert.Equal(1, inventory.BufferedCount);
        Assert.Equal(1, inventory.UnbufferedCount);

        var brcb = inventory.ReportControls.Single(x => x.Buffered);
        Assert.Equal("OCR7SR12PROT/LLN0.BR.brcbA01", brcb.Reference);
        Assert.Equal("OCR7SR12PROT/LLN0.DataSet", brcb.DataSetReference);
        Assert.Contains("RptID", brcb.Attributes);
        Assert.Contains("DatSet", brcb.Attributes);
    }
}
