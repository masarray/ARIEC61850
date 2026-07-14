namespace AR.Iec61850.Scl.Export;

public enum SclSchemaProfile
{
    Edition2V31,
    Edition1V16,
    Edition1V15,
    Edition1V14
}

public sealed record SclSchemaProfileDescriptor(
    SclSchemaProfile Profile,
    string DisplayName,
    string CliName,
    SclEdition Edition,
    string SchemaVersion,
    string SchemaDate,
    string DefaultExtension,
    string? RootVersion,
    string? RootRevision,
    bool SupportsTriggerGi,
    bool SupportsReservationTime)
{
    public bool IsEdition2 => Edition == SclEdition.Edition2;
}

public static class SclSchemaProfiles
{
    private static readonly IReadOnlyList<SclSchemaProfileDescriptor> AllProfiles =
    [
        new(
            SclSchemaProfile.Edition2V31,
            "Edition 2 (Schema V3.1)",
            "edition2-v3.1",
            SclEdition.Edition2,
            "3.1",
            "2012/10/22",
            ".iid",
            "2007",
            "B",
            SupportsTriggerGi: true,
            SupportsReservationTime: true),
        new(
            SclSchemaProfile.Edition1V16,
            "Edition 1 (Schema V1.6)",
            "edition1-v1.6",
            SclEdition.Edition1,
            "1.6",
            "2013/08/14",
            ".icd",
            null,
            null,
            SupportsTriggerGi: false,
            SupportsReservationTime: false),
        new(
            SclSchemaProfile.Edition1V15,
            "Edition 1 (Schema V1.5)",
            "edition1-v1.5",
            SclEdition.Edition1,
            "1.5",
            "2005/08/11",
            ".icd",
            null,
            null,
            SupportsTriggerGi: false,
            SupportsReservationTime: false),
        new(
            SclSchemaProfile.Edition1V14,
            "Edition 1 (Schema V1.4)",
            "edition1-v1.4",
            SclEdition.Edition1,
            "1.4",
            "2005/08/11",
            ".icd",
            null,
            null,
            SupportsTriggerGi: false,
            SupportsReservationTime: false)
    ];

    public static IReadOnlyList<SclSchemaProfileDescriptor> All => AllProfiles;

    public static SclSchemaProfileDescriptor Get(SclSchemaProfile profile)
        => AllProfiles.First(candidate => candidate.Profile == profile);

    public static SclSchemaProfile Parse(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "ed2" or "edition2" or "edition-2" or "edition2-v3.1" or "ed2-v3.1" or "3.1" or "v3.1"
                => SclSchemaProfile.Edition2V31,
            "ed1" or "edition1" or "edition-1" or "edition1-v1.6" or "ed1-v1.6" or "1.6" or "v1.6"
                => SclSchemaProfile.Edition1V16,
            "edition1-v1.5" or "ed1-v1.5" or "1.5" or "v1.5"
                => SclSchemaProfile.Edition1V15,
            "edition1-v1.4" or "ed1-v1.4" or "1.4" or "v1.4"
                => SclSchemaProfile.Edition1V14,
            _ => throw new ArgumentException(
                "SCL schema must be edition2-v3.1, edition1-v1.6, edition1-v1.5, or edition1-v1.4.")
        };
}
