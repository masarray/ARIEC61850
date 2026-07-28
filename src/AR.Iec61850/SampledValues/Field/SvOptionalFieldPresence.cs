namespace AR.Iec61850.SampledValues.Field;

public sealed record SvOptionalFieldPresence
{
    public bool DataSetReferencePresent { get; init; }
    public bool ReferenceTimePresent { get; init; }
    public bool SampleRatePresent { get; init; }
    public bool SampleModePresent { get; init; }
    public bool SvIdPresent { get; init; }
    public bool SampleSynchronizationPresent { get; init; }
    public IReadOnlyList<string> PresentFields { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AbsentOptionalFields { get; init; } = Array.Empty<string>();

    public string Summary => AbsentOptionalFields.Count == 0
        ? "All modeled optional fields are present"
        : $"Optional fields not present on wire: {string.Join(", ", AbsentOptionalFields)}";
}

public static class SvOptionalFieldInspector
{
    public static SvOptionalFieldPresence Inspect(SampledValueAsdu asdu)
    {
        ArgumentNullException.ThrowIfNull(asdu);
        var present = new List<string>();
        var absent = new List<string>();

        Add("svID", !string.IsNullOrWhiteSpace(asdu.SvId), required: true, present, absent);
        Add("datSet", !string.IsNullOrWhiteSpace(asdu.DataSetReference), required: false, present, absent);
        Add("refrTm", asdu.ReferenceTime.HasValue, required: false, present, absent);
        Add("smpRate", asdu.SampleRate.HasValue, required: false, present, absent);
        Add("smpMod", asdu.SampleMode.HasValue, required: false, present, absent);
        Add("smpSynch", true, required: true, present, absent);

        return new SvOptionalFieldPresence
        {
            DataSetReferencePresent = !string.IsNullOrWhiteSpace(asdu.DataSetReference),
            ReferenceTimePresent = asdu.ReferenceTime.HasValue,
            SampleRatePresent = asdu.SampleRate.HasValue,
            SampleModePresent = asdu.SampleMode.HasValue,
            SvIdPresent = !string.IsNullOrWhiteSpace(asdu.SvId),
            SampleSynchronizationPresent = true,
            PresentFields = present,
            AbsentOptionalFields = absent
        };
    }

    private static void Add(
        string name,
        bool isPresent,
        bool required,
        ICollection<string> present,
        ICollection<string> absent)
    {
        if (isPresent)
            present.Add(name);
        else if (!required)
            absent.Add(name);
    }
}
