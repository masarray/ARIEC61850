using AR.Iec61850.Scl;

namespace AR.Iec61850.SampledValues.Profiles;

public static class SvExpectedStreamConfigurationFactory
{
    public static SvExpectedStreamConfiguration Create(SampledValuesPublisherProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new SvExpectedStreamConfiguration
        {
            EtherType = 0x88BA,
            AppId = profile.AppId,
            DestinationMac = profile.Destination.ToString(),
            VlanId = profile.Vlan?.VlanId,
            VlanPriority = profile.Vlan?.PriorityCodePoint,
            SvId = profile.Stream.SvId,
            DataSetReference = profile.Stream.DataSetReference,
            ConfigurationRevision = profile.Stream.ConfigurationRevision,
            AsduPerFrame = profile.AsduPerFrame,
            PayloadBytesPerAsdu = profile.PayloadLayout.PayloadByteLength,
            DeclaredSampleRate = profile.Stream.SampleRate == 0 ? null : profile.Stream.SampleRate,
            DeclaredSampleMode = MapSampleMode(profile.Stream.SampleMode),
            DataSetSignature = profile.Entries.Select(ToSignature).ToArray()
        };
    }

    private static ushort? MapSampleMode(string sampleMode)
        => string.IsNullOrWhiteSpace(sampleMode) ? null : sampleMode.Trim() switch
        {
            "SmpPerPeriod" => 0,
            "SmpPerSec" => 1,
            "SecPerSmp" => 2,
            _ => null
        };

    private static SvDatasetElementSignature ToSignature(SclDataSetEntry entry)
        => new() { BType = entry.BType, Cdc = entry.Cdc, IsQuality = entry.IsQuality, IsTimestamp = entry.IsTimestamp };
}
