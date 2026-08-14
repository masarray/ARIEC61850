namespace AR.Iec61850.Discovery;

/// <summary>
/// Stable, literal selections over the typed signal catalog. These selectors do not infer
/// IEC 61850 semantics or reinterpret reconciliation outcomes; they only filter fields and
/// statuses already owned by the engine catalog.
/// </summary>
public enum Iec61850SignalCatalogSelection
{
    All,
    Design,
    LiveOnly,
    StaticDataSetMandatory,
    MandatoryPrimary,
    Operational,
    EngineeringOnly,
    VerifiedPresent,
    ConfirmedAbsent,
    UnreconciledDesign,
    ReconciledDesign,
    DesignOnly
}

public static class Iec61850SignalCatalogQuery
{
    public static IReadOnlyList<Iec61850SignalDescriptor> Select(
        this Iec61850SignalCatalogDocument catalog,
        Iec61850SignalCatalogSelection selection)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return Ordered(catalog.Signals.Where(signal => selection switch
        {
            Iec61850SignalCatalogSelection.All => true,
            Iec61850SignalCatalogSelection.Design => IsDesign(signal),
            Iec61850SignalCatalogSelection.LiveOnly => signal.ResolutionStatus == Iec61850SignalCatalogResolutionStatus.LiveOnly,
            Iec61850SignalCatalogSelection.StaticDataSetMandatory => signal.IsStaticDataSetMandatory,
            Iec61850SignalCatalogSelection.MandatoryPrimary => IsMandatoryPrimary(signal),
            Iec61850SignalCatalogSelection.Operational => signal.IsOperationalCandidate,
            Iec61850SignalCatalogSelection.EngineeringOnly => signal.IsEngineeringOnly,
            Iec61850SignalCatalogSelection.VerifiedPresent => signal.IsVerifiedPresent,
            Iec61850SignalCatalogSelection.ConfirmedAbsent => signal.IsConfirmedAbsent,
            Iec61850SignalCatalogSelection.UnreconciledDesign => IsDesign(signal) && signal.LiveStatus is null,
            Iec61850SignalCatalogSelection.ReconciledDesign => IsDesign(signal) && signal.LiveStatus.HasValue,
            Iec61850SignalCatalogSelection.DesignOnly => signal.LiveStatus == Iec61850DesignLiveStatus.DesignOnly,
            _ => throw new ArgumentOutOfRangeException(nameof(selection), selection, "Unknown signal catalog selection.")
        }));
    }

    public static IReadOnlyList<Iec61850SignalDescriptor> GetDesignSignals(this Iec61850SignalCatalogDocument catalog)
        => catalog.Select(Iec61850SignalCatalogSelection.Design);

    public static IReadOnlyList<Iec61850SignalDescriptor> GetLiveOnlySignals(this Iec61850SignalCatalogDocument catalog)
        => catalog.Select(Iec61850SignalCatalogSelection.LiveOnly);

    public static IReadOnlyList<Iec61850SignalDescriptor> GetStaticDataSetMandatorySignals(this Iec61850SignalCatalogDocument catalog)
        => catalog.Select(Iec61850SignalCatalogSelection.StaticDataSetMandatory);

    public static IReadOnlyList<Iec61850SignalDescriptor> GetMandatoryPrimarySignals(this Iec61850SignalCatalogDocument catalog)
        => catalog.Select(Iec61850SignalCatalogSelection.MandatoryPrimary);

    public static IReadOnlyList<Iec61850SignalDescriptor> GetOperationalSignals(this Iec61850SignalCatalogDocument catalog)
        => catalog.Select(Iec61850SignalCatalogSelection.Operational);

    public static IReadOnlyList<Iec61850SignalDescriptor> GetEngineeringOnlySignals(this Iec61850SignalCatalogDocument catalog)
        => catalog.Select(Iec61850SignalCatalogSelection.EngineeringOnly);

    public static IReadOnlyList<Iec61850SignalDescriptor> GetVerifiedPresentSignals(this Iec61850SignalCatalogDocument catalog)
        => catalog.Select(Iec61850SignalCatalogSelection.VerifiedPresent);

    public static IReadOnlyList<Iec61850SignalDescriptor> GetConfirmedAbsentSignals(this Iec61850SignalCatalogDocument catalog)
        => catalog.Select(Iec61850SignalCatalogSelection.ConfirmedAbsent);

    public static IReadOnlyList<Iec61850SignalDescriptor> GetUnreconciledDesignSignals(this Iec61850SignalCatalogDocument catalog)
        => catalog.Select(Iec61850SignalCatalogSelection.UnreconciledDesign);

    public static IReadOnlyList<Iec61850SignalDescriptor> GetReconciledDesignSignals(this Iec61850SignalCatalogDocument catalog)
        => catalog.Select(Iec61850SignalCatalogSelection.ReconciledDesign);

    public static IReadOnlyList<Iec61850SignalDescriptor> GetDesignOnlySignals(this Iec61850SignalCatalogDocument catalog)
        => catalog.Select(Iec61850SignalCatalogSelection.DesignOnly);

    public static IReadOnlyList<Iec61850SignalDescriptor> GetSignalsByLiveStatus(
        this Iec61850SignalCatalogDocument catalog,
        Iec61850DesignLiveStatus status)
        => Filter(catalog, signal => signal.LiveStatus == status);

    public static IReadOnlyList<Iec61850SignalDescriptor> GetSignalsByResolutionStatus(
        this Iec61850SignalCatalogDocument catalog,
        Iec61850SignalCatalogResolutionStatus status)
        => Filter(catalog, signal => signal.ResolutionStatus == status);

    public static IReadOnlyList<Iec61850SignalDescriptor> GetSignalsBySemanticRole(
        this Iec61850SignalCatalogDocument catalog,
        Iec61850DataAttributeSemanticRole role)
        => Filter(catalog, signal => signal.SemanticRole == role);

    public static IReadOnlyList<Iec61850SignalDescriptor> GetSignalsByFunctionalConstraint(
        this Iec61850SignalCatalogDocument catalog,
        string? functionalConstraint)
    {
        var fc = NormalizeToken(functionalConstraint);
        return fc.Length == 0
            ? Array.Empty<Iec61850SignalDescriptor>()
            : Filter(catalog, signal => TokenEquals(signal.FunctionalConstraint, fc));
    }

    public static IReadOnlyList<Iec61850SignalDescriptor> GetSignalsByCdc(
        this Iec61850SignalCatalogDocument catalog,
        string? cdc)
    {
        var normalized = NormalizeToken(cdc);
        return normalized.Length == 0
            ? Array.Empty<Iec61850SignalDescriptor>()
            : Filter(catalog, signal => TokenEquals(signal.Cdc, normalized));
    }

    public static IReadOnlyList<Iec61850SignalDescriptor> GetSignalsByLogicalNodeClass(
        this Iec61850SignalCatalogDocument catalog,
        string? logicalNodeClass)
    {
        var normalized = NormalizeToken(logicalNodeClass);
        return normalized.Length == 0
            ? Array.Empty<Iec61850SignalDescriptor>()
            : Filter(catalog, signal => TokenEquals(signal.LogicalNodeClass, normalized));
    }

    public static IReadOnlyList<Iec61850SignalDescriptor> GetSignalsByDataSetReference(
        this Iec61850SignalCatalogDocument catalog,
        string? dataSetReference)
    {
        var reference = NormalizeReference(dataSetReference);
        return reference.Length == 0
            ? Array.Empty<Iec61850SignalDescriptor>()
            : Filter(catalog, signal => signal.DataSetMemberships.Any(membership =>
                ReferenceEquals(membership.DataSetReference, reference)));
    }

    public static IReadOnlyList<Iec61850SignalDescriptor> GetSignalsByReportControlReference(
        this Iec61850SignalCatalogDocument catalog,
        string? reportControlReference)
    {
        var reference = NormalizeReference(reportControlReference);
        return reference.Length == 0
            ? Array.Empty<Iec61850SignalDescriptor>()
            : Filter(catalog, signal => signal.ReportMemberships.Any(membership =>
                ReferenceEquals(membership.ReportControlReference, reference)));
    }

    public static IReadOnlyList<Iec61850SignalDescriptor> GetSignalsByDesignReference(
        this Iec61850SignalCatalogDocument catalog,
        string? designReference)
    {
        var reference = NormalizeReference(designReference);
        return reference.Length == 0
            ? Array.Empty<Iec61850SignalDescriptor>()
            : Filter(catalog, signal => ReferenceEquals(signal.DesignReference, reference));
    }

    public static IReadOnlyList<Iec61850SignalDescriptor> GetSignalsByObservedMmsReference(
        this Iec61850SignalCatalogDocument catalog,
        string? observedMmsReference)
    {
        var reference = NormalizeReference(observedMmsReference);
        return reference.Length == 0
            ? Array.Empty<Iec61850SignalDescriptor>()
            : Filter(catalog, signal => ReferenceEquals(signal.ObservedMmsReference, reference));
    }

    private static IReadOnlyList<Iec61850SignalDescriptor> Filter(
        Iec61850SignalCatalogDocument catalog,
        Func<Iec61850SignalDescriptor, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(predicate);
        return Ordered(catalog.Signals.Where(predicate));
    }

    private static IReadOnlyList<Iec61850SignalDescriptor> Ordered(IEnumerable<Iec61850SignalDescriptor> signals)
        => signals
            .OrderBy(signal => signal.ResolutionStatus == Iec61850SignalCatalogResolutionStatus.LiveOnly ? 1 : 0)
            .ThenBy(SortReference, StringComparer.OrdinalIgnoreCase)
            .ThenBy(signal => signal.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string SortReference(Iec61850SignalDescriptor signal)
        => FirstNonEmpty(
            signal.CanonicalMmsReference,
            signal.EffectiveMmsReference,
            signal.DesignReference,
            signal.ObservedMmsReference,
            signal.ObservedReference);

    private static bool IsDesign(Iec61850SignalDescriptor signal)
        => signal.ResolutionStatus != Iec61850SignalCatalogResolutionStatus.LiveOnly;

    private static bool IsMandatoryPrimary(Iec61850SignalDescriptor signal)
        => signal.IsStaticDataSetMandatory &&
           signal.SemanticRole == Iec61850DataAttributeSemanticRole.PrimaryValue;

    private static bool TokenEquals(string? left, string right)
        => string.Equals(NormalizeToken(left), right, StringComparison.OrdinalIgnoreCase);

    private static bool ReferenceEquals(string? left, string right)
        => string.Equals(NormalizeReference(left), right, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeToken(string? value)
        => (value ?? string.Empty).Trim();

    private static string NormalizeReference(string? value)
        => (value ?? string.Empty).Trim().Replace('\\', '/');

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
