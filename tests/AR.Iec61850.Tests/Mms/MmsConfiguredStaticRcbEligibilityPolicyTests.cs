using AR.Iec61850.Mms;

namespace AR.Iec61850.Tests.Mms;

public sealed class MmsConfiguredStaticRcbEligibilityPolicyTests
{
    [Fact]
    public void Exact_Explicit_Free_Brcb_Remains_Preferred()
    {
        var snapshot = StaticSnapshot(
            buffered: true,
            availability: MmsRcbOperationalAvailability.Available,
            confidence: MmsRcbAvailabilityConfidence.Exact,
            reservationTime: "0");

        var result = MmsConfiguredStaticRcbEligibilityPolicy.Evaluate(
            snapshot,
            allowCallerOwned: true,
            allowReducedMissingReservationEvidence: false);

        Assert.True(result.IsEligible);
        Assert.Equal(MmsConfiguredStaticRcbEligibilityKind.ExplicitFree, result.Kind);
        Assert.False(result.UsesReducedEvidence);
    }

    [Fact]
    public void Missing_Brcb_Reservation_Evidence_Is_Blocked_By_Default()
    {
        var snapshot = StaticSnapshot(
            buffered: true,
            availability: MmsRcbOperationalAvailability.Unknown,
            confidence: MmsRcbAvailabilityConfidence.Reduced,
            reservationTime: string.Empty);

        var result = MmsConfiguredStaticRcbEligibilityPolicy.Evaluate(
            snapshot,
            allowCallerOwned: true,
            allowReducedMissingReservationEvidence: false);

        Assert.False(result.IsEligible);
        Assert.Equal(MmsConfiguredStaticRcbEligibilityKind.Blocked, result.Kind);
    }

    [Fact]
    public void Explicit_OptIn_Allows_Only_Genuinely_Missing_Brcb_Reservation_Evidence()
    {
        var snapshot = StaticSnapshot(
            buffered: true,
            availability: MmsRcbOperationalAvailability.Unknown,
            confidence: MmsRcbAvailabilityConfidence.Reduced,
            reservationTime: "-");

        var result = MmsConfiguredStaticRcbEligibilityPolicy.Evaluate(
            snapshot,
            allowCallerOwned: true,
            allowReducedMissingReservationEvidence: true);

        Assert.True(result.IsEligible);
        Assert.True(result.RequiresWrite);
        Assert.True(result.UsesReducedEvidence);
        Assert.Equal(
            MmsConfiguredStaticRcbEligibilityKind.ReducedMissingReservationEvidence,
            result.Kind);
    }

    [Fact]
    public void Malformed_Reservation_Evidence_Is_Not_Downgraded_To_Missing()
    {
        var snapshot = StaticSnapshot(
            buffered: true,
            availability: MmsRcbOperationalAvailability.Unknown,
            confidence: MmsRcbAvailabilityConfidence.Reduced,
            reservationTime: "vendor-weird");

        var result = MmsConfiguredStaticRcbEligibilityPolicy.Evaluate(
            snapshot,
            allowCallerOwned: true,
            allowReducedMissingReservationEvidence: true);

        Assert.False(result.IsEligible);
        Assert.Contains("neither explicitly free", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Positive_Owner_Evidence_Always_Blocks_Reduced_Path()
    {
        var snapshot = StaticSnapshot(
            buffered: true,
            availability: MmsRcbOperationalAvailability.Unknown,
            confidence: MmsRcbAvailabilityConfidence.Reduced,
            reservationTime: string.Empty,
            owner: "01020304");

        var result = MmsConfiguredStaticRcbEligibilityPolicy.Evaluate(
            snapshot,
            allowCallerOwned: true,
            allowReducedMissingReservationEvidence: true);

        Assert.False(result.IsEligible);
        Assert.Contains("ownership/reservation", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_Urcb_Resv_Can_Use_The_Same_Explicit_Reduced_Policy()
    {
        var snapshot = StaticSnapshot(
            buffered: false,
            availability: MmsRcbOperationalAvailability.Unknown,
            confidence: MmsRcbAvailabilityConfidence.Reduced,
            reservationState: string.Empty);

        var result = MmsConfiguredStaticRcbEligibilityPolicy.Evaluate(
            snapshot,
            allowCallerOwned: true,
            allowReducedMissingReservationEvidence: true);

        Assert.True(result.IsEligible);
        Assert.Equal(
            MmsConfiguredStaticRcbEligibilityKind.ReducedMissingReservationEvidence,
            result.Kind);
    }

    [Fact]
    public void Dynamic_Empty_Rcb_Is_Never_Promoted_By_Configured_Static_Policy()
    {
        var snapshot = StaticSnapshot(
            buffered: true,
            availability: MmsRcbOperationalAvailability.NoDataSet,
            confidence: MmsRcbAvailabilityConfidence.Exact,
            reservationTime: string.Empty,
            dataSetReference: string.Empty,
            members: Array.Empty<MmsDataSetDirectoryMember>());

        var result = MmsConfiguredStaticRcbEligibilityPolicy.Evaluate(
            snapshot,
            allowCallerOwned: true,
            allowReducedMissingReservationEvidence: true);

        Assert.False(result.IsEligible);
        Assert.Contains("configured static", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static MmsRcbAvailabilitySnapshot StaticSnapshot(
        bool buffered,
        MmsRcbOperationalAvailability availability,
        MmsRcbAvailabilityConfidence confidence,
        string reservationTime = "",
        string reservationState = "",
        string owner = "",
        string dataSetReference = "LD0/LLN0.dsA",
        IReadOnlyList<MmsDataSetDirectoryMember>? members = null)
    {
        members ??=
        [
            new MmsDataSetDirectoryMember
            {
                Domain = "LD0",
                MmsItemName = "GGIO1$ST$Ind1",
                UserReference = "LD0/GGIO1.Ind1",
                FunctionalConstraint = "ST"
            }
        ];

        return new MmsRcbAvailabilitySnapshot
        {
            Reference = buffered ? "LD0/LLN0.BR.B01" : "LD0/LLN0.RP.U01",
            Domain = "LD0",
            LogicalNode = "LLN0",
            Name = buffered ? "B01" : "U01",
            Buffered = buffered,
            DataSetReference = dataSetReference,
            DataSetProbeState = MmsRcbDataSetProbeState.ReadSucceeded,
            EnabledState = "false",
            ReservationTimeSeconds = reservationTime,
            ReservationState = reservationState,
            Owner = owner,
            DataSetDirectoryRead = true,
            DataSetDirectorySuccess = members.Count > 0,
            DataSetMemberCount = members.Count,
            DataSetMembers = members,
            Availability = availability,
            Confidence = confidence
        };
    }
}
