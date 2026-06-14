namespace AR.Iec61850.IedDiscovery.ViewModels;

public sealed record MetricRow(string Label, string Value, string Accent = "");
public sealed record LogicalDeviceRow(string LogicalDevice, int LogicalNodeCount, int PointCount);
public sealed record DataSetRow(string Reference, int MemberCount, string UsedByReports, string UsedByGoose, string UsedBySv);
public sealed record ReportControlRow(string Reference, string Mode, string DataSet, string EnabledState, string ReservationState, string ConfRev, string Status);
public sealed record WarningRow(string Severity, string Message);
