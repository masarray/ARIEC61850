namespace AR.Iec61850.Comtrade;

public sealed record ComtradeConfiguration(
    string StationName,
    string DeviceId,
    int RevisionYear,
    int TotalChannels,
    int AnalogChannelCount,
    int DigitalChannelCount,
    double LineFrequencyHz,
    IReadOnlyList<ComtradeAnalogChannel> AnalogChannels,
    IReadOnlyList<ComtradeSampleRate> SampleRates,
    string DataFileType,
    double TimeMultiplier);
