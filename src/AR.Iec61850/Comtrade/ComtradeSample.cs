namespace AR.Iec61850.Comtrade;

public sealed record ComtradeSample(int Number, double TimestampSeconds, IReadOnlyList<double> AnalogValues);
