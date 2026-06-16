namespace AR.Iec61850.Comtrade;

public sealed record ComtradeAnalogChannel(
    int Index,
    string Name,
    string Phase,
    string CircuitComponent,
    string Unit,
    double Multiplier,
    double Offset,
    double Skew,
    double Minimum,
    double Maximum,
    double Primary,
    double Secondary,
    string ScalingIdentifier);
