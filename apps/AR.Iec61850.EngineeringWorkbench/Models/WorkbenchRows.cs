namespace AR.Iec61850.EngineeringWorkbench.Models;

public sealed record MetricRow(string Label, string Value, string Hint = "");
public sealed record FindingRow(string Severity, string Area, string Code, string Message, string Recommendation = "");
public sealed record SclNodeRow(string Reference, string LnClass, string Type, int DataSets, int Reports, int Goose, int SampledValues, int ExtRefs);
public sealed record ProcessBusRow(string Kind, string Expected, string Status, string AppId, string Destination, string Vlan, string ConfRev, int Packets, int Findings);
public sealed record GooseRow(string Status, string Expected, string Observed, string AppId, int Packets, string State, string Sequence, int Gaps, int Duplicates, int Timeouts, int Score);
public sealed record SampledValuesRow(string Status, string Expected, string Observed, string AppId, int Packets, string SampleCount, int Gaps, int Missed, int Duplicates, int OutOfOrder, string Sync, int Score);
public sealed record MmsGateRow(string Gate, string Status, string Message);
public sealed record EvidenceRow(string Artifact, string Status, string Path);
