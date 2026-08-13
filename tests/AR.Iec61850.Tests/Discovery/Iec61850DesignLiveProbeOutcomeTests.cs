using AR.Iec61850.Discovery;

namespace AR.Iec61850.Tests.Discovery;

public sealed class Iec61850DesignLiveProbeOutcomeTests
{
    [Theory]
    [InlineData(Iec61850ExactProbeStatus.Unreadable, Iec61850DesignLiveStatus.Unreadable)]
    [InlineData(Iec61850ExactProbeStatus.TransportFailure, Iec61850DesignLiveStatus.TransportFailure)]
    public async Task Probe_Failure_Outcomes_Are_Not_Reported_As_Absent(
        Iec61850ExactProbeStatus probeStatus,
        Iec61850DesignLiveStatus expectedStatus)
    {
        var result = await Iec61850DesignLiveReconciler.ReconcileAsync(
            BuildMandatoryDesign(),
            new LiveIedModelDiscoveryDocument { Source = "LiveMmsDiscovery", IedName = "IED" },
            new FixedProbe(probeStatus));

        var point = Assert.Single(result.Points, x => x.IsDataSetMandatory && x.IsPrimaryValue);
        Assert.Equal(expectedStatus, point.Status);
        Assert.Equal(0, result.AbsentCount);
        Assert.False(result.HasConfirmedAbsence);
    }

    private static LiveIedModelDiscoveryDocument BuildMandatoryDesign()
        => new()
        {
            Source = "SclWorkspace",
            IedName = "IED",
            LogicalDevices = new[]
            {
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IEDLD0",
                    LogicalNodes = new[]
                    {
                        new LiveIedLogicalNodeModel
                        {
                            Name = "MMTR1",
                            DataObjects = new[]
                            {
                                new LiveIedDataObjectModel
                                {
                                    Reference = "IEDLD0/MMTR1.SupWh",
                                    Name = "SupWh",
                                    InferredCdc = "BCR"
                                }
                            }
                        }
                    }
                }
            },
            DataSets = new[]
            {
                new LiveIedDataSetModel
                {
                    Reference = "IEDLD0/LLN0.dsEnergy",
                    Domain = "IEDLD0",
                    LogicalNode = "LLN0",
                    Name = "dsEnergy",
                    MemberCount = 1,
                    Members = new[]
                    {
                        new LiveIedDataSetMemberModel
                        {
                            Index = 0,
                            Reference = "IEDLD0/MMTR1.SupWh",
                            FunctionalConstraint = "ST"
                        }
                    }
                }
            }
        };

    private sealed class FixedProbe : IIec61850ExactReadProbe
    {
        private readonly Iec61850ExactProbeStatus _status;

        public FixedProbe(Iec61850ExactProbeStatus status) => _status = status;

        public Task<Iec61850ExactProbeEvidence> ProbeAsync(
            string mmsReference,
            string functionalConstraint,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new Iec61850ExactProbeEvidence
            {
                Status = _status,
                MmsReference = mmsReference,
                FunctionalConstraint = functionalConstraint,
                Message = _status.ToString()
            });
    }
}