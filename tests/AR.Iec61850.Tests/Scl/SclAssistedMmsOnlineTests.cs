using AR.Iec61850.Scl;

namespace AR.Iec61850.Tests.Scl;

public sealed class SclAssistedMmsOnlineTests
{
    [Fact]
    public void DomainInventoryReader_Projects_LdName_And_IedPlusInst_For_Selected_AccessPoint()
    {
        const string xml = """
            <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
              <IED name="IED01">
                <AccessPoint name="AP1">
                  <Server>
                    <LDevice inst="LD0" />
                    <LDevice inst="PROT" ldName="CUSTOM_PROTECTION" />
                    <LDevice inst="LD0" />
                  </Server>
                </AccessPoint>
                <AccessPoint name="AP2">
                  <Server>
                    <LDevice inst="SHOULD_NOT_LEAK" />
                  </Server>
                </AccessPoint>
              </IED>
              <IED name="IED02">
                <AccessPoint name="AP1">
                  <Server>
                    <LDevice inst="OTHER" />
                  </Server>
                </AccessPoint>
              </IED>
            </SCL>
            """;

        var inventory = SclMmsDomainInventoryReader.Read(xml, "IED01", "AP1");

        Assert.True(inventory.IsSuccess, string.Join(" | ", inventory.Errors));
        Assert.Equal(new[] { "IED01LD0", "CUSTOM_PROTECTION" }, inventory.ExpectedDomains);
        Assert.Single(inventory.Warnings);
        Assert.DoesNotContain(inventory.ExpectedDomains, domain => domain.Contains("SHOULD_NOT_LEAK", StringComparison.Ordinal));
        Assert.DoesNotContain(inventory.ExpectedDomains, domain => domain.Contains("OTHER", StringComparison.Ordinal));
    }

    [Fact]
    public void DomainInventoryReader_Fails_Closed_When_Selected_AccessPoint_Has_No_Server()
    {
        const string xml = """
            <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
              <IED name="IED01">
                <AccessPoint name="AP1" />
              </IED>
            </SCL>
            """;

        var inventory = SclMmsDomainInventoryReader.Read(xml, "IED01", "AP1");

        Assert.False(inventory.IsSuccess);
        Assert.Empty(inventory.ExpectedDomains);
        Assert.Contains(inventory.Errors, error => error.Contains("no direct Server", StringComparison.Ordinal));
    }

    [Fact]
    public void DomainReconciliation_Missing_Expected_Domain_Is_Incompatible_And_Extra_Is_Preserved()
    {
        var result = SclMmsDomainReconciler.Reconcile(
            new[] { "IED01LD0", "IED01PROT" },
            new[] { "IED01LD0", "VENDOR_EXTRA" });

        Assert.False(result.IsCompatible);
        Assert.False(result.IsExactMatch);
        Assert.Equal(new[] { "IED01LD0" }, result.MatchedDomains);
        Assert.Equal(new[] { "IED01PROT" }, result.MissingExpectedDomains);
        Assert.Equal(new[] { "VENDOR_EXTRA" }, result.ExtraObservedDomains);
    }

    [Fact]
    public void DomainReconciliation_Extra_Online_Domain_Is_Evidence_Only()
    {
        var result = SclMmsDomainReconciler.Reconcile(
            new[] { "IED01LD0" },
            new[] { "VENDOR_EXTRA", "ied01ld0" });

        Assert.True(result.IsCompatible);
        Assert.False(result.IsExactMatch);
        Assert.Empty(result.MissingExpectedDomains);
        Assert.Equal(new[] { "VENDOR_EXTRA" }, result.ExtraObservedDomains);
    }

    [Fact]
    public async Task SclAssistedConnect_Returns_Typed_InvalidPlan_Without_Network_SideEffects()
    {
        await using var session = new AR.Iec61850.Mms.MmsClientSession();
        var result = await session.ConnectSclAssistedAsync(
            new SclAssistedMmsAssociationPlan(),
            new SclMmsDomainInventory
            {
                IedName = "IED01",
                AccessPointName = "AP1",
                Errors = new[] { "synthetic invalid design inventory" }
            });

        Assert.Equal(SclAssistedMmsOnlineStatus.InvalidPlan, result.Status);
        Assert.False(result.AssociationSucceeded);
        Assert.False(result.DomainInventorySucceeded);
        Assert.False(result.SessionRemainsOpen);
        Assert.False(session.IsTcpConnected);
    }
}
