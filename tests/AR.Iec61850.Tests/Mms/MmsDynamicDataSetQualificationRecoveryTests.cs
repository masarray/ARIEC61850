using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsDynamicDataSetQualificationRecoveryTests
{
    private static readonly string[] Expected =
    [
        "IEDLD/LLN0$ST$A$stVal",
        "IEDLD/LLN0$ST$B$stVal"
    ];

    [Fact]
    public void ExactCurrentRunResidue_MayBeDeletedOnHealthyFreshAssociation()
    {
        var allowed = MmsDynamicDataSetQualificationRecoveryPolicy.CanDeleteExactResidue(
            namePresent: true,
            directoryReadable: true,
            expectedMemberReferences: Expected,
            observedMemberReferences: Expected,
            associationHealthy: true,
            out var reason);

        Assert.True(allowed, reason);
        Assert.Contains("exact ordered", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MemberMismatch_NeverAuthorizesDelete()
    {
        var allowed = MmsDynamicDataSetQualificationRecoveryPolicy.CanDeleteExactResidue(
            namePresent: true,
            directoryReadable: true,
            expectedMemberReferences: Expected,
            observedMemberReferences:
            [
                Expected[0],
                "IEDLD/LLN0$ST$OTHER$stVal"
            ],
            associationHealthy: true,
            out var reason);

        Assert.False(allowed);
        Assert.Contains("does not exactly match", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NameOnlyWithoutReadableDirectory_NeverAuthorizesDelete()
    {
        var allowed = MmsDynamicDataSetQualificationRecoveryPolicy.CanDeleteExactResidue(
            namePresent: true,
            directoryReadable: false,
            expectedMemberReferences: Expected,
            observedMemberReferences: Array.Empty<string>(),
            associationHealthy: true,
            out var reason);

        Assert.False(allowed);
        Assert.Contains("readable directory", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClosedRecovery_RequiresBothAbsenceProofsAndHealthyAssociation()
    {
        Assert.True(MmsDynamicDataSetQualificationRecoveryPolicy.IsRecoveryClosed(
            namePresent: false,
            namespaceAbsenceProven: true,
            directoryReadable: false,
            directoryAbsenceProven: true,
            associationHealthy: true,
            out var passReason), passReason);

        Assert.False(MmsDynamicDataSetQualificationRecoveryPolicy.IsRecoveryClosed(
            namePresent: false,
            namespaceAbsenceProven: false,
            directoryReadable: false,
            directoryAbsenceProven: true,
            associationHealthy: true,
            out var namespaceReason));
        Assert.Contains("namespace absence was not proven", namespaceReason, StringComparison.OrdinalIgnoreCase);

        Assert.False(MmsDynamicDataSetQualificationRecoveryPolicy.IsRecoveryClosed(
            namePresent: false,
            namespaceAbsenceProven: true,
            directoryReadable: false,
            directoryAbsenceProven: false,
            associationHealthy: true,
            out var directoryReason));
        Assert.Contains("direct-directory absence was not proven", directoryReason, StringComparison.OrdinalIgnoreCase);

        Assert.False(MmsDynamicDataSetQualificationRecoveryPolicy.IsRecoveryClosed(
            namePresent: true,
            namespaceAbsenceProven: false,
            directoryReadable: false,
            directoryAbsenceProven: true,
            associationHealthy: true,
            out _));
        Assert.False(MmsDynamicDataSetQualificationRecoveryPolicy.IsRecoveryClosed(
            namePresent: false,
            namespaceAbsenceProven: true,
            directoryReadable: true,
            directoryAbsenceProven: false,
            associationHealthy: true,
            out _));
        Assert.False(MmsDynamicDataSetQualificationRecoveryPolicy.IsRecoveryClosed(
            namePresent: false,
            namespaceAbsenceProven: true,
            directoryReadable: false,
            directoryAbsenceProven: true,
            associationHealthy: false,
            out _));
    }

    [Fact]
    public void ExactOrderedMemberMatch_IsOrderSensitive()
    {
        Assert.True(MmsDynamicDataSetQualificationRecoveryPolicy.ExactOrderedMembersMatch(Expected, Expected));
        Assert.False(MmsDynamicDataSetQualificationRecoveryPolicy.ExactOrderedMembersMatch(
            Expected,
            Expected.Reverse().ToArray()));
    }
}
