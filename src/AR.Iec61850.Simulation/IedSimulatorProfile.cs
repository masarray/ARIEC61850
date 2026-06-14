namespace AR.Iec61850.Simulation;

public sealed record IedSimulatorProfile
{
    public string Name { get; init; } = "AR Demo IED";
    public string Vendor { get; init; } = "ARIEC61850";
    public string Edition { get; init; } = "IEC 61850 Ed2-style lab profile";
    public IReadOnlyList<IedSimulatorLogicalDevice> LogicalDevices { get; init; } = Array.Empty<IedSimulatorLogicalDevice>();
    public IReadOnlyList<IedSimulatorDataSet> DataSets { get; init; } = Array.Empty<IedSimulatorDataSet>();
    public IReadOnlyList<IedSimulatorReportControlBlock> ReportControlBlocks { get; init; } = Array.Empty<IedSimulatorReportControlBlock>();

    public int LogicalNodeCount => LogicalDevices.Sum(x => x.LogicalNodes.Count);
    public int PointCount => LogicalDevices.SelectMany(x => x.LogicalNodes).Sum(x => x.Points.Count);
    public int DataSetMemberCount => DataSets.Sum(x => x.Members.Count);

    public static IedSimulatorProfile CreateDefaultFeederProfile()
    {
        var points = new[]
        {
            IedSimulatorPoint.Measurement("MMXU1.PhV.phsA.cVal.mag.f", "MX", "V", 230000, 1500, 0),
            IedSimulatorPoint.Measurement("MMXU1.PhV.phsB.cVal.mag.f", "MX", "V", 230000, 1500, 120),
            IedSimulatorPoint.Measurement("MMXU1.PhV.phsC.cVal.mag.f", "MX", "V", 230000, 1500, 240),
            IedSimulatorPoint.Measurement("MMXU1.A.phsA.cVal.mag.f", "MX", "A", 245, 18, 10),
            IedSimulatorPoint.Measurement("MMXU1.A.phsB.cVal.mag.f", "MX", "A", 242, 18, 130),
            IedSimulatorPoint.Measurement("MMXU1.A.phsC.cVal.mag.f", "MX", "A", 248, 18, 250),
            IedSimulatorPoint.Status("XCBR1.Pos.stVal", "ST", "closed"),
            IedSimulatorPoint.Status("XCBR1.Pos.q", "ST", "valid"),
            IedSimulatorPoint.Status("CSWI1.Pos.stVal", "ST", "closed"),
            IedSimulatorPoint.Status("PTOC1.Str.general", "ST", "false"),
            IedSimulatorPoint.Status("PTOC1.Op.general", "ST", "false")
        };

        return new IedSimulatorProfile
        {
            Name = "AR Demo Feeder IED",
            LogicalDevices = new[]
            {
                new IedSimulatorLogicalDevice
                {
                    Name = "IED1LD0",
                    LogicalNodes = new[]
                    {
                        new IedSimulatorLogicalNode
                        {
                            Name = "LLN0",
                            LnClass = "LLN0",
                            Points = Array.Empty<IedSimulatorPoint>()
                        },
                        new IedSimulatorLogicalNode
                        {
                            Name = "MMXU1",
                            LnClass = "MMXU",
                            Points = points.Where(x => x.Reference.StartsWith("MMXU1.", StringComparison.OrdinalIgnoreCase)).ToArray()
                        },
                        new IedSimulatorLogicalNode
                        {
                            Name = "XCBR1",
                            LnClass = "XCBR",
                            Points = points.Where(x => x.Reference.StartsWith("XCBR1.", StringComparison.OrdinalIgnoreCase)).ToArray()
                        },
                        new IedSimulatorLogicalNode
                        {
                            Name = "CSWI1",
                            LnClass = "CSWI",
                            Points = points.Where(x => x.Reference.StartsWith("CSWI1.", StringComparison.OrdinalIgnoreCase)).ToArray()
                        },
                        new IedSimulatorLogicalNode
                        {
                            Name = "PTOC1",
                            LnClass = "PTOC",
                            Points = points.Where(x => x.Reference.StartsWith("PTOC1.", StringComparison.OrdinalIgnoreCase)).ToArray()
                        }
                    }
                }
            },
            DataSets = new[]
            {
                new IedSimulatorDataSet
                {
                    Reference = "IED1LD0/LLN0.dsMeas",
                    Members = points.Where(x => x.FunctionalConstraint == "MX").Select(x => $"IED1LD0/{x.Reference}").ToArray()
                },
                new IedSimulatorDataSet
                {
                    Reference = "IED1LD0/LLN0.dsStatus",
                    Members = points.Where(x => x.FunctionalConstraint == "ST").Select(x => $"IED1LD0/{x.Reference}").ToArray()
                }
            },
            ReportControlBlocks = new[]
            {
                new IedSimulatorReportControlBlock
                {
                    Reference = "IED1LD0/LLN0.RP.rptStatus01",
                    Buffered = false,
                    DataSetReference = "IED1LD0/LLN0.dsStatus",
                    ReportId = "AR_RP_STATUS_01",
                    ConfRev = 1,
                    TriggerOptions = "data-change, quality-change, GI",
                    OptionalFields = "seqNum, timeStamp, reasonCode, dataSet, confRev"
                },
                new IedSimulatorReportControlBlock
                {
                    Reference = "IED1LD0/LLN0.BR.rptMeas01",
                    Buffered = true,
                    DataSetReference = "IED1LD0/LLN0.dsMeas",
                    ReportId = "AR_BR_MEAS_01",
                    ConfRev = 1,
                    BufferTimeMs = 100,
                    IntegrityPeriodMs = 1000,
                    TriggerOptions = "data-change, integrity, GI",
                    OptionalFields = "seqNum, entryId, timeStamp, reasonCode, dataSet, confRev"
                }
            }
        };
    }
}

public sealed record IedSimulatorLogicalDevice
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<IedSimulatorLogicalNode> LogicalNodes { get; init; } = Array.Empty<IedSimulatorLogicalNode>();
}

public sealed record IedSimulatorLogicalNode
{
    public string Name { get; init; } = string.Empty;
    public string LnClass { get; init; } = string.Empty;
    public IReadOnlyList<IedSimulatorPoint> Points { get; init; } = Array.Empty<IedSimulatorPoint>();
}

public sealed record IedSimulatorPoint
{
    public string Reference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string Kind { get; init; } = "status";
    public string Unit { get; init; } = string.Empty;
    public double BaseValue { get; init; }
    public double Amplitude { get; init; }
    public double PhaseDeg { get; init; }
    public string InitialValue { get; init; } = string.Empty;

    public static IedSimulatorPoint Measurement(string reference, string functionalConstraint, string unit, double baseValue, double amplitude, double phaseDeg)
        => new()
        {
            Reference = reference,
            FunctionalConstraint = functionalConstraint,
            Kind = "measurement",
            Unit = unit,
            BaseValue = baseValue,
            Amplitude = amplitude,
            PhaseDeg = phaseDeg,
            InitialValue = baseValue.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
        };

    public static IedSimulatorPoint Status(string reference, string functionalConstraint, string value)
        => new()
        {
            Reference = reference,
            FunctionalConstraint = functionalConstraint,
            Kind = "status",
            InitialValue = value
        };
}

public sealed record IedSimulatorDataSet
{
    public string Reference { get; init; } = string.Empty;
    public IReadOnlyList<string> Members { get; init; } = Array.Empty<string>();
}

public sealed record IedSimulatorReportControlBlock
{
    public string Reference { get; init; } = string.Empty;
    public bool Buffered { get; init; }
    public string DataSetReference { get; init; } = string.Empty;
    public string ReportId { get; init; } = string.Empty;
    public int ConfRev { get; init; } = 1;
    public int BufferTimeMs { get; init; }
    public int IntegrityPeriodMs { get; init; }
    public string TriggerOptions { get; init; } = string.Empty;
    public string OptionalFields { get; init; } = string.Empty;

    public string Mode => Buffered ? "BRCB" : "URCB";
}
