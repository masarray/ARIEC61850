namespace AR.Iec61850.Discovery;

public enum Iec61850DataAttributeSemanticRole
{
    Other,
    PrimaryValue,
    FrozenValue,
    Quality,
    Timestamp
}

public enum LiveIedDataSetMemberResolutionStatus
{
    Unresolved,
    ExactAttribute,
    TemplateResolved,
    DiscoveredAttributes,
    CdcFallback,
    FunctionalConstraintMismatch,
    Ambiguous
}

public sealed class LiveIedResolvedDataSetAttributeModel
{
    public string Reference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string MmsReference { get; init; } = string.Empty;
    public string MmsItemName { get; init; } = string.Empty;
    public string Cdc { get; init; } = string.Empty;
    public string SclBType { get; init; } = string.Empty;
    public string MmsType { get; init; } = string.Empty;
    public Iec61850DataAttributeSemanticRole SemanticRole { get; init; }
    public LiveIedDiscoveryConfidenceLevel Confidence { get; init; } = LiveIedDiscoveryConfidenceLevel.Unknown;
    public string Source { get; init; } = string.Empty;
    public bool IsSyntheticFallback { get; init; }
    public bool IsPrimaryValue => SemanticRole == Iec61850DataAttributeSemanticRole.PrimaryValue;
}

public sealed class LiveIedDataSetMemberSemanticBinding
{
    public string DataSetReference { get; init; } = string.Empty;
    public int Index { get; init; }
    public string OriginalReference { get; init; } = string.Empty;
    public string CanonicalReference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string Cdc { get; init; } = string.Empty;
    public LiveIedDataSetMemberResolutionStatus ResolutionStatus { get; init; }
    public IReadOnlyList<LiveIedResolvedDataSetAttributeModel> ResolvedAttributes { get; init; } = Array.Empty<LiveIedResolvedDataSetAttributeModel>();
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public bool IsResolved => ResolutionStatus is not LiveIedDataSetMemberResolutionStatus.Unresolved
        and not LiveIedDataSetMemberResolutionStatus.FunctionalConstraintMismatch
        and not LiveIedDataSetMemberResolutionStatus.Ambiguous;

    public LiveIedResolvedDataSetAttributeModel? PrimaryValue
        => ResolvedAttributes.Count(x => x.IsPrimaryValue) == 1
            ? ResolvedAttributes.Single(x => x.IsPrimaryValue)
            : null;

    public string PrimaryValueReference => PrimaryValue?.Reference ?? string.Empty;
    public string PrimaryValueMmsReference => PrimaryValue?.MmsReference ?? string.Empty;
}

public sealed class LiveIedDataSetSemanticBindingModel
{
    public string DataSetReference { get; init; } = string.Empty;
    public IReadOnlyList<LiveIedDataSetMemberSemanticBinding> Members { get; init; } = Array.Empty<LiveIedDataSetMemberSemanticBinding>();
}

public sealed class LiveIedDataSetSemanticBindingDocument
{
    public IReadOnlyList<LiveIedDataSetSemanticBindingModel> DataSets { get; init; } = Array.Empty<LiveIedDataSetSemanticBindingModel>();
    public IEnumerable<LiveIedDataSetMemberSemanticBinding> Members => DataSets.SelectMany(x => x.Members);

    public LiveIedDataSetMemberSemanticBinding? Find(string dataSetReference, int memberIndex)
        => DataSets
            .FirstOrDefault(x => string.Equals(x.DataSetReference, dataSetReference, StringComparison.OrdinalIgnoreCase))?
            .Members.FirstOrDefault(x => x.Index == memberIndex);
}

/// <summary>
/// Resolves original FCD/FCDA DataSet members to typed DataAttribute targets.
/// Original member identity and list ordering remain protocol evidence; semantic
/// leaves are application bindings only and never additional DataSet members.
///
/// Structured FCDA members are intentionally supported. A member such as A.phsA or
/// PPV.phsAB can identify an intermediate structured component rather than a final DA.
/// In that case only typed descendants below the exact member boundary are considered.
/// The resolver never widens the member to sibling phases and never invents a leaf.
/// </summary>
public static class Iec61850DataSetSemanticBindingResolver
{
    private static readonly HashSet<string> FunctionalConstraints = new(StringComparer.OrdinalIgnoreCase)
    {
        "ST", "MX", "CO", "SP", "CF", "DC", "SG", "SE", "SV", "EX", "SR", "OR", "BL",
        "RP", "BR", "LG", "GO", "GS", "MS", "US"
    };

    public static LiveIedDataSetSemanticBindingDocument Resolve(LiveIedModelDiscoveryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var dataObjects = document.LogicalDevices
            .SelectMany(ld => ld.LogicalNodes)
            .SelectMany(ln => ln.DataObjects)
            .ToArray();

        return new LiveIedDataSetSemanticBindingDocument
        {
            DataSets = document.DataSets
                .Select(dataSet => new LiveIedDataSetSemanticBindingModel
                {
                    DataSetReference = dataSet.Reference,
                    Members = dataSet.Members
                        .Select(member => ResolveMember(document, dataSet, member, dataObjects))
                        .ToArray()
                })
                .ToArray()
        };
    }

    private static LiveIedDataSetMemberSemanticBinding ResolveMember(
        LiveIedModelDiscoveryDocument document,
        LiveIedDataSetModel dataSet,
        LiveIedDataSetMemberModel member,
        IReadOnlyList<LiveIedDataObjectModel> dataObjects)
    {
        var reference = NormalizeReference(member.Reference);
        var fc = NormalizeFunctionalConstraint(member.FunctionalConstraint);
        if (string.IsNullOrWhiteSpace(reference))
            return Unresolved(dataSet, member, "DataSet member has no object reference.");

        var objectMatches = dataObjects
            .Select(dataObject => new { DataObject = dataObject, Reference = NormalizeReference(dataObject.Reference) })
            .Where(candidate => IsReferenceInsideDataObject(reference, candidate.Reference))
            .OrderByDescending(candidate => candidate.Reference.Length)
            .ToArray();
        if (objectMatches.Length == 0)
            return Unresolved(dataSet, member, $"No DataObject model matches canonical reference '{reference}'.");

        var bestLength = objectMatches[0].Reference.Length;
        var bestMatches = objectMatches.Where(candidate => candidate.Reference.Length == bestLength).ToArray();
        if (bestMatches.Length != 1)
        {
            return BuildBinding(
                dataSet,
                member,
                string.Empty,
                LiveIedDataSetMemberResolutionStatus.Ambiguous,
                Array.Empty<LiveIedResolvedDataSetAttributeModel>(),
                $"Multiple DataObjects match canonical DataSet member '{reference}'.");
        }

        var dataObject = bestMatches[0].DataObject;
        var dataObjectReference = bestMatches[0].Reference;
        var cdc = dataObject.InferredCdc?.Trim() ?? string.Empty;
        var isObjectLevelMember = string.Equals(reference, dataObjectReference, StringComparison.OrdinalIgnoreCase);
        var fcCompatibleAttributes = dataObject.Attributes
            .Where(attribute => IsFunctionalConstraintCompatible(fc, attribute.FunctionalConstraint))
            .ToArray();

        if (!isObjectLevelMember)
        {
            var exact = fcCompatibleAttributes
                .Where(attribute => string.Equals(
                    NormalizeReference(attribute.ObjectReference),
                    reference,
                    StringComparison.OrdinalIgnoreCase))
                .Select(attribute => ToResolvedAttribute(dataObject, attribute, fc))
                .ToArray();
            if (exact.Length == 1)
            {
                return BuildBinding(
                    dataSet,
                    member,
                    cdc,
                    LiveIedDataSetMemberResolutionStatus.ExactAttribute,
                    PromoteUniqueFallbackPrimary(exact),
                    $"Explicit DataAttribute member matched '{exact[0].Reference}'.");
            }
            if (exact.Length > 1)
            {
                return BuildBinding(
                    dataSet,
                    member,
                    cdc,
                    LiveIedDataSetMemberResolutionStatus.Ambiguous,
                    exact,
                    $"Explicit DataAttribute member '{reference}' matched more than one attribute model.");
            }

            // IEC 61850 FCDA may stop at an intermediate structured component. Siemens
            // measurement DataSets commonly contain A.phsA / A.phsB / A.phsC and
            // PPV.phsAB-style members while SCL DataTypeTemplates expose final leaves such
            // as cVal.mag.f below those components. Restrict expansion to descendants of
            // the exact static member boundary so no sibling phase can be captured.
            var descendants = fcCompatibleAttributes
                .Where(attribute => IsDescendantReference(
                    NormalizeReference(attribute.ObjectReference),
                    reference))
                .Select(attribute => ToResolvedAttribute(dataObject, attribute, fc))
                .OrderBy(attribute => SemanticOrder(attribute.SemanticRole))
                .ThenBy(attribute => attribute.Reference, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (descendants.Length > 0)
            {
                return BuildExpandedBinding(
                    document,
                    dataSet,
                    member,
                    cdc,
                    descendants,
                    $"Structured DataSet member '{reference}' expanded only to typed descendants below the exact member boundary.");
            }

            if (dataObject.Attributes.Count > 0 && fcCompatibleAttributes.Length == 0)
            {
                return BuildBinding(
                    dataSet,
                    member,
                    cdc,
                    LiveIedDataSetMemberResolutionStatus.FunctionalConstraintMismatch,
                    Array.Empty<LiveIedResolvedDataSetAttributeModel>(),
                    $"DataObject exists, but no attribute is compatible with FC={fc}.");
            }

            return Unresolved(
                dataSet,
                member,
                $"Explicit or structured DataAttribute member '{reference}' is not present in the resolved DataObject model.",
                cdc);
        }

        if (fcCompatibleAttributes.Length > 0)
        {
            var resolved = fcCompatibleAttributes
                .Select(attribute => ToResolvedAttribute(dataObject, attribute, fc))
                .OrderBy(attribute => SemanticOrder(attribute.SemanticRole))
                .ThenBy(attribute => attribute.Reference, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return BuildExpandedBinding(
                document,
                dataSet,
                member,
                cdc,
                resolved,
                $"DataObject-level FCD '{reference}' expanded to FC-compatible typed attributes.");
        }

        if (dataObject.Attributes.Count > 0)
        {
            return BuildBinding(
                dataSet,
                member,
                cdc,
                LiveIedDataSetMemberResolutionStatus.FunctionalConstraintMismatch,
                Array.Empty<LiveIedResolvedDataSetAttributeModel>(),
                $"DataObject has attribute evidence, but none is compatible with FC={fc}; CDC fallback is intentionally not used over conflicting typed evidence.");
        }

        var fallback = BuildCdcFallback(dataObject, fc).ToArray();
        if (fallback.Length > 0)
        {
            return BuildBinding(
                dataSet,
                member,
                cdc,
                LiveIedDataSetMemberResolutionStatus.CdcFallback,
                fallback,
                $"No attribute-level evidence was available. Standard CDC={cdc} semantics supplied read candidates without changing the original DataSet member.");
        }

        return Unresolved(
            dataSet,
            member,
            $"DataObject '{dataObject.Reference}' has no attribute-level evidence and CDC={cdc} has no safe fallback mapping.",
            cdc);
    }

    private static LiveIedDataSetMemberSemanticBinding BuildExpandedBinding(
        LiveIedModelDiscoveryDocument document,
        LiveIedDataSetModel dataSet,
        LiveIedDataSetMemberModel member,
        string cdc,
        IReadOnlyList<LiveIedResolvedDataSetAttributeModel> attributes,
        string evidencePrefix)
    {
        var resolved = PromoteUniqueFallbackPrimary(attributes);
        var primaryCount = resolved.Count(attribute => attribute.IsPrimaryValue);
        var fromScl = IsSclProjection(document, resolved);
        var status = primaryCount > 1
            ? LiveIedDataSetMemberResolutionStatus.Ambiguous
            : fromScl
                ? LiveIedDataSetMemberResolutionStatus.TemplateResolved
                : LiveIedDataSetMemberResolutionStatus.DiscoveredAttributes;
        var evidence = evidencePrefix + (fromScl
            ? $" Authority is SCL DataTypeTemplates for CDC={cdc}."
            : $" Authority is discovered attribute-level MMS evidence for CDC={cdc}.");

        if (primaryCount > 1)
            evidence += " More than one primary-value candidate was found; no primary target is selected.";
        else if (primaryCount == 1)
            evidence += $" Unique primary runtime leaf is '{resolved.Single(attribute => attribute.IsPrimaryValue).Reference}'.";
        else
            evidence += " No unique primary-value leaf is available; the static membership remains unresolved for scalar acquisition.";

        return BuildBinding(dataSet, member, cdc, status, resolved, evidence);
    }

    /// <summary>
    /// Prefer explicit semantic PrimaryValue roles. When a legacy/shallow typed model has
    /// no such role, promote exactly one engine-approved primary-value-bearing attribute.
    /// Multiple candidates remain unresolved rather than selecting by ordering.
    /// </summary>
    private static IReadOnlyList<LiveIedResolvedDataSetAttributeModel> PromoteUniqueFallbackPrimary(
        IReadOnlyList<LiveIedResolvedDataSetAttributeModel> attributes)
    {
        if (attributes.Count(attribute => attribute.IsPrimaryValue) != 0)
            return attributes;

        var candidates = attributes
            .Where(Iec61850ProbeValuePolicy.IsPrimaryValueBearing)
            .ToArray();
        if (candidates.Length != 1)
            return attributes;

        var selected = candidates[0];
        return attributes
            .Select(attribute => ReferenceEquals(attribute.Reference, selected.Reference)
                ? CloneWithRole(attribute, Iec61850DataAttributeSemanticRole.PrimaryValue)
                : attribute)
            .ToArray();
    }

    private static LiveIedResolvedDataSetAttributeModel CloneWithRole(
        LiveIedResolvedDataSetAttributeModel attribute,
        Iec61850DataAttributeSemanticRole role)
        => new()
        {
            Reference = attribute.Reference,
            FunctionalConstraint = attribute.FunctionalConstraint,
            MmsReference = attribute.MmsReference,
            MmsItemName = attribute.MmsItemName,
            Cdc = attribute.Cdc,
            SclBType = attribute.SclBType,
            MmsType = attribute.MmsType,
            SemanticRole = role,
            Confidence = attribute.Confidence,
            Source = attribute.Source,
            IsSyntheticFallback = attribute.IsSyntheticFallback
        };

    private static IEnumerable<LiveIedResolvedDataSetAttributeModel> BuildCdcFallback(
        LiveIedDataObjectModel dataObject,
        string functionalConstraint)
    {
        if (!string.Equals(dataObject.InferredCdc, "BCR", StringComparison.OrdinalIgnoreCase))
            yield break;

        yield return BuildFallbackAttribute(dataObject, functionalConstraint, "actVal", "INT64", Iec61850DataAttributeSemanticRole.PrimaryValue);
        yield return BuildFallbackAttribute(dataObject, functionalConstraint, "frVal", "INT64", Iec61850DataAttributeSemanticRole.FrozenValue);
        yield return BuildFallbackAttribute(dataObject, functionalConstraint, "q", "Quality", Iec61850DataAttributeSemanticRole.Quality);
        yield return BuildFallbackAttribute(dataObject, functionalConstraint, "t", "Timestamp", Iec61850DataAttributeSemanticRole.Timestamp);
    }

    private static LiveIedResolvedDataSetAttributeModel BuildFallbackAttribute(
        LiveIedDataObjectModel dataObject,
        string functionalConstraint,
        string attributePath,
        string sclBType,
        Iec61850DataAttributeSemanticRole role)
    {
        var reference = NormalizeReference(dataObject.Reference) + "." + attributePath;
        var target = BuildMmsTarget(reference, functionalConstraint);
        return new LiveIedResolvedDataSetAttributeModel
        {
            Reference = reference,
            FunctionalConstraint = functionalConstraint,
            MmsReference = target.Reference,
            MmsItemName = target.ItemName,
            Cdc = dataObject.InferredCdc,
            SclBType = sclBType,
            SemanticRole = role,
            Confidence = LiveIedDiscoveryConfidenceLevel.Medium,
            Source = "CDC.BCR",
            IsSyntheticFallback = true
        };
    }

    private static LiveIedResolvedDataSetAttributeModel ToResolvedAttribute(
        LiveIedDataObjectModel dataObject,
        LiveIedDataAttributeModel attribute,
        string memberFunctionalConstraint)
    {
        var reference = NormalizeReference(attribute.ObjectReference);
        var fc = NormalizeFunctionalConstraint(attribute.FunctionalConstraint);
        if (string.IsNullOrWhiteSpace(fc))
            fc = memberFunctionalConstraint;
        var target = BuildMmsTarget(reference, fc);
        var source = string.IsNullOrWhiteSpace(attribute.Source) ? attribute.TypeSource : attribute.Source;
        var confidence = string.Equals(source, "SCL.DataTypeTemplates", StringComparison.OrdinalIgnoreCase)
            ? LiveIedDiscoveryConfidenceLevel.Exact
            : attribute.TypeConfidence is LiveIedDiscoveryConfidenceLevel.Exact or LiveIedDiscoveryConfidenceLevel.High
                ? attribute.TypeConfidence
                : LiveIedDiscoveryConfidenceLevel.High;

        return new LiveIedResolvedDataSetAttributeModel
        {
            Reference = reference,
            FunctionalConstraint = fc,
            MmsReference = target.Reference,
            MmsItemName = target.ItemName,
            Cdc = dataObject.InferredCdc,
            SclBType = attribute.SclBType,
            MmsType = attribute.MmsType,
            SemanticRole = ClassifySemanticRole(dataObject.InferredCdc, attribute.AttributePath),
            Confidence = confidence,
            Source = source,
            IsSyntheticFallback = false
        };
    }

    private static Iec61850DataAttributeSemanticRole ClassifySemanticRole(string cdc, string attributePath)
    {
        var path = (attributePath ?? string.Empty).Trim().Replace('$', '.').Trim('.');
        var lower = path.ToLowerInvariant();
        var leaf = lower.Contains('.') ? lower[(lower.LastIndexOf('.') + 1)..] : lower;

        if (leaf == "q")
            return Iec61850DataAttributeSemanticRole.Quality;
        if (leaf == "t")
            return Iec61850DataAttributeSemanticRole.Timestamp;

        if (string.Equals(cdc, "BCR", StringComparison.OrdinalIgnoreCase))
        {
            if (lower == "actval")
                return Iec61850DataAttributeSemanticRole.PrimaryValue;
            if (lower == "frval")
                return Iec61850DataAttributeSemanticRole.FrozenValue;
        }

        if (lower is "stval" or "general" or "posval" or "actval")
            return Iec61850DataAttributeSemanticRole.PrimaryValue;

        // Canonical engineering values are preferred over their instantaneous siblings.
        // Alternate-reference policy can still recover cVal <-> instCVal or mag <-> instMag
        // at live verification time without making an FCD with both representations ambiguous.
        if (lower.EndsWith(".mag.f", StringComparison.Ordinal) &&
            !lower.EndsWith(".instcval.mag.f", StringComparison.Ordinal) &&
            !lower.EndsWith(".instmag.f", StringComparison.Ordinal))
        {
            return Iec61850DataAttributeSemanticRole.PrimaryValue;
        }

        return Iec61850DataAttributeSemanticRole.Other;
    }

    private static LiveIedDataSetMemberSemanticBinding BuildBinding(
        LiveIedDataSetModel dataSet,
        LiveIedDataSetMemberModel member,
        string cdc,
        LiveIedDataSetMemberResolutionStatus status,
        IReadOnlyList<LiveIedResolvedDataSetAttributeModel> attributes,
        string evidence)
        => new()
        {
            DataSetReference = dataSet.Reference,
            Index = member.Index,
            OriginalReference = member.Reference,
            CanonicalReference = NormalizeReference(member.Reference),
            FunctionalConstraint = NormalizeFunctionalConstraint(member.FunctionalConstraint),
            Cdc = cdc,
            ResolutionStatus = status,
            ResolvedAttributes = attributes,
            Evidence = new[] { evidence }
        };

    private static LiveIedDataSetMemberSemanticBinding Unresolved(
        LiveIedDataSetModel dataSet,
        LiveIedDataSetMemberModel member,
        string evidence,
        string cdc = "")
        => BuildBinding(
            dataSet,
            member,
            cdc,
            LiveIedDataSetMemberResolutionStatus.Unresolved,
            Array.Empty<LiveIedResolvedDataSetAttributeModel>(),
            evidence);

    private static bool IsSclProjection(
        LiveIedModelDiscoveryDocument document,
        IReadOnlyList<LiveIedResolvedDataSetAttributeModel> attributes)
        => document.Source.Contains("Scl", StringComparison.OrdinalIgnoreCase) ||
           attributes.Any(attribute => attribute.Source.Contains("SCL", StringComparison.OrdinalIgnoreCase));

    private static bool IsReferenceInsideDataObject(string memberReference, string dataObjectReference)
        => string.Equals(memberReference, dataObjectReference, StringComparison.OrdinalIgnoreCase) ||
           memberReference.StartsWith(dataObjectReference + ".", StringComparison.OrdinalIgnoreCase);

    private static bool IsDescendantReference(string candidateReference, string memberReference)
        => candidateReference.StartsWith(memberReference + ".", StringComparison.OrdinalIgnoreCase);

    private static bool IsFunctionalConstraintCompatible(string memberFc, string attributeFc)
    {
        if (string.IsNullOrWhiteSpace(memberFc))
            return true;
        if (string.IsNullOrWhiteSpace(attributeFc))
            return false;
        return string.Equals(memberFc, NormalizeFunctionalConstraint(attributeFc), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ReferenceEquals(string? left, string? right)
        => string.Equals(
            NormalizeReference(left),
            NormalizeReference(right),
            StringComparison.OrdinalIgnoreCase);

    private static (string Reference, string ItemName) BuildMmsTarget(
        string userReference,
        string functionalConstraint)
    {
        var reference = NormalizeReference(userReference);
        var slash = reference.IndexOf('/');
        if (slash <= 0 || slash >= reference.Length - 1)
            return (string.Empty, string.Empty);

        var domain = reference[..slash];
        var logicalPath = reference[(slash + 1)..];
        var firstDot = logicalPath.IndexOf('.');
        if (firstDot <= 0 || firstDot >= logicalPath.Length - 1)
            return (string.Empty, string.Empty);

        var logicalNode = logicalPath[..firstDot];
        var objectAndAttribute = logicalPath[(firstDot + 1)..].Replace('.', '$');
        var fc = NormalizeFunctionalConstraint(functionalConstraint);
        if (string.IsNullOrWhiteSpace(fc))
            return (string.Empty, string.Empty);

        var itemName = $"{logicalNode}${fc}${objectAndAttribute}";
        return ($"{domain}/{itemName}", itemName);
    }

    private static string NormalizeReference(string? value)
    {
        var text = StripDisplayFunctionalConstraint((value ?? string.Empty).Trim());
        if (text.Length == 0)
            return string.Empty;

        text = text.Replace('\\', '/');
        while (text.Contains("//", StringComparison.Ordinal))
            text = text.Replace("//", "/", StringComparison.Ordinal);
        text = text.Trim('/');

        var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return string.Empty;
        if (parts.Length == 1)
            return NormalizeLogicalPath(parts[0]);

        string domain;
        string logicalPath;
        if (parts.Length >= 3)
        {
            domain = parts[0] + parts[1];
            logicalPath = string.Join('/', parts.Skip(2));
        }
        else
        {
            domain = parts[0];
            logicalPath = parts[1];
        }

        return $"{domain}/{NormalizeLogicalPath(logicalPath)}";
    }

    private static string NormalizeLogicalPath(string logicalPath)
    {
        var text = logicalPath.Trim().Trim('.', '$');
        var dollarParts = text.Split('$', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (dollarParts.Length <= 1)
            return text;

        var normalized = new List<string>(dollarParts.Length) { dollarParts[0] };
        var start = dollarParts.Length > 2 && FunctionalConstraints.Contains(dollarParts[1]) ? 2 : 1;
        for (var index = start; index < dollarParts.Length; index++)
            normalized.Add(dollarParts[index]);
        return string.Join('.', normalized);
    }

    private static string StripDisplayFunctionalConstraint(string text)
    {
        if (!text.EndsWith(']'))
            return text;
        var open = text.LastIndexOf('[');
        if (open < 0 || open >= text.Length - 2)
            return text;
        var token = text[(open + 1)..^1].Trim();
        return FunctionalConstraints.Contains(token) ? text[..open].TrimEnd() : text;
    }

    private static string NormalizeFunctionalConstraint(string? value)
        => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static int SemanticOrder(Iec61850DataAttributeSemanticRole role)
        => role switch
        {
            Iec61850DataAttributeSemanticRole.PrimaryValue => 0,
            Iec61850DataAttributeSemanticRole.FrozenValue => 1,
            Iec61850DataAttributeSemanticRole.Quality => 2,
            Iec61850DataAttributeSemanticRole.Timestamp => 3,
            _ => 4
        };
}
