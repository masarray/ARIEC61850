using AR.Iec61850.Scl;

namespace AR.Iec61850.Mms;

public enum MmsSclRcbFamilyResolutionKind
{
    None,
    ExactLiveInstance,
    IndexedFamily
}

public sealed class MmsSclRcbFamilyResolution
{
    public string DeclaredReference { get; init; } = string.Empty;
    public bool Indexed { get; init; }
    public MmsSclRcbFamilyResolutionKind Kind { get; init; }
    public IReadOnlyList<MmsReportControlCandidate> Candidates { get; init; } = Array.Empty<MmsReportControlCandidate>();
    public string Message { get; init; } = string.Empty;

    public bool IsSuccess => Candidates.Count > 0;
    public bool IsExact => Kind == MmsSclRcbFamilyResolutionKind.ExactLiveInstance;
    public IReadOnlyList<string> MatchedReferences => Candidates.Select(candidate => candidate.Reference).ToArray();
}

/// <summary>
/// Reconciles one declarative SCL ReportControl identity with concrete RCB
/// instances observed from the live MMS directory.
///
/// The resolver never fabricates indexed RCB names. For indexed SCL controls it
/// accepts only live names that preserve the declared domain/LN/FC/name and may
/// add a non-empty decimal suffix to the declared control-block name. This
/// supports live index widths such as 1, 01, or 001 without hard-coding a
/// vendor rule. IEC 61850 object identity is compared case-sensitively.
/// </summary>
public static class MmsSclRcbFamilyResolver
{
    public static MmsSclRcbFamilyResolution Resolve(
        SclReportControl reportControl,
        IEnumerable<MmsReportControlCandidate> liveCandidates)
    {
        ArgumentNullException.ThrowIfNull(reportControl);
        ArgumentNullException.ThrowIfNull(liveCandidates);

        var declared = ParseDeclaredIdentity(reportControl);
        if (declared == null)
        {
            return new MmsSclRcbFamilyResolution
            {
                DeclaredReference = reportControl.ControlBlockReference,
                Indexed = reportControl.Indexed,
                Kind = MmsSclRcbFamilyResolutionKind.None,
                Message = $"SCL ReportControl reference '{reportControl.ControlBlockReference}' could not be parsed into canonical domain/LN/FC/name identity."
            };
        }

        var live = liveCandidates
            .Where(candidate => candidate != null)
            .Select(candidate => new { Candidate = candidate, Identity = ParseLiveIdentity(candidate) })
            .Where(item => item.Identity != null && SameContainer(declared, item.Identity!))
            .ToArray();

        var exact = live
            .Where(item => string.Equals(item.Identity!.Name, declared.Name, StringComparison.Ordinal))
            .Select(item => item.Candidate)
            .DistinctBy(candidate => candidate.Reference, StringComparer.Ordinal)
            .OrderBy(candidate => candidate.Reference, StringComparer.Ordinal)
            .ToArray();

        if (!reportControl.Indexed)
        {
            return new MmsSclRcbFamilyResolution
            {
                DeclaredReference = reportControl.ControlBlockReference,
                Indexed = false,
                Kind = exact.Length == 0 ? MmsSclRcbFamilyResolutionKind.None : MmsSclRcbFamilyResolutionKind.ExactLiveInstance,
                Candidates = exact,
                Message = exact.Length == 0
                    ? $"Non-indexed SCL ReportControl '{reportControl.ControlBlockReference}' has no exact live RCB instance. Family expansion is not permitted."
                    : $"Non-indexed SCL ReportControl '{reportControl.ControlBlockReference}' matched {exact.Length} exact live RCB instance(s)."
            };
        }

        var indexed = live
            .Where(item => HasDecimalInstanceSuffix(declared.Name, item.Identity!.Name))
            .Select(item => item.Candidate);

        var family = exact
            .Concat(indexed)
            .DistinctBy(candidate => candidate.Reference, StringComparer.Ordinal)
            .OrderBy(candidate => candidate.Reference, StringComparer.Ordinal)
            .ToArray();

        if (family.Length == 0)
        {
            return new MmsSclRcbFamilyResolution
            {
                DeclaredReference = reportControl.ControlBlockReference,
                Indexed = true,
                Kind = MmsSclRcbFamilyResolutionKind.None,
                Message = $"Indexed SCL ReportControl family '{reportControl.ControlBlockReference}' has no concrete live MMS RCB instance. No indexed name was synthesized."
            };
        }

        var kind = indexed.Any() || exact.Length > 1
            ? MmsSclRcbFamilyResolutionKind.IndexedFamily
            : MmsSclRcbFamilyResolutionKind.ExactLiveInstance;

        return new MmsSclRcbFamilyResolution
        {
            DeclaredReference = reportControl.ControlBlockReference,
            Indexed = true,
            Kind = kind,
            Candidates = family,
            Message = kind == MmsSclRcbFamilyResolutionKind.ExactLiveInstance
                ? $"Indexed SCL ReportControl '{reportControl.ControlBlockReference}' currently exposes one exact concrete live RCB instance."
                : $"Indexed SCL ReportControl family '{reportControl.ControlBlockReference}' resolved to {family.Length} concrete live MMS RCB instance(s)."
        };
    }

    private static RcbIdentity? ParseDeclaredIdentity(SclReportControl reportControl)
    {
        var parsed = ParseReference(reportControl.ControlBlockReference);
        if (parsed != null)
            return parsed;

        if (string.IsNullOrWhiteSpace(reportControl.IedName) ||
            string.IsNullOrWhiteSpace(reportControl.LdInst) ||
            string.IsNullOrWhiteSpace(reportControl.LogicalNodePath) ||
            string.IsNullOrWhiteSpace(reportControl.Name))
            return null;

        return new RcbIdentity(
            reportControl.IedName + reportControl.LdInst,
            reportControl.LogicalNodePath,
            reportControl.Buffered ? "BR" : "RP",
            reportControl.Name);
    }

    private static RcbIdentity? ParseLiveIdentity(MmsReportControlCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.Domain) &&
            !string.IsNullOrWhiteSpace(candidate.LogicalNode) &&
            !string.IsNullOrWhiteSpace(candidate.FunctionalConstraint) &&
            !string.IsNullOrWhiteSpace(candidate.Name))
        {
            return new RcbIdentity(
                candidate.Domain.Trim(),
                candidate.LogicalNode.Trim(),
                candidate.FunctionalConstraint.Trim(),
                candidate.Name.Trim());
        }

        return ParseReference(candidate.Reference);
    }

    private static RcbIdentity? ParseReference(string? reference)
    {
        var text = (reference ?? string.Empty).Trim();
        var slash = text.IndexOf('/');
        if (slash <= 0 || slash == text.Length - 1)
            return null;

        var domain = text[..slash].Trim();
        var tail = text[(slash + 1)..].Trim();
        string[] parts;
        if (tail.Contains('$'))
            parts = tail.Split('$', StringSplitOptions.None);
        else
            parts = tail.Split('.', StringSplitOptions.None);

        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
            return null;

        return new RcbIdentity(domain, parts[0].Trim(), parts[1].Trim(), parts[2].Trim());
    }

    private static bool SameContainer(RcbIdentity left, RcbIdentity right)
        => string.Equals(left.Domain, right.Domain, StringComparison.Ordinal) &&
           string.Equals(left.LogicalNode, right.LogicalNode, StringComparison.Ordinal) &&
           string.Equals(left.FunctionalConstraint, right.FunctionalConstraint, StringComparison.Ordinal);

    private static bool HasDecimalInstanceSuffix(string declaredName, string liveName)
    {
        if (liveName.Length <= declaredName.Length ||
            !liveName.StartsWith(declaredName, StringComparison.Ordinal))
            return false;

        var suffix = liveName.AsSpan(declaredName.Length);
        foreach (var character in suffix)
        {
            if (character is < '0' or > '9')
                return false;
        }

        return true;
    }

    private sealed record RcbIdentity(string Domain, string LogicalNode, string FunctionalConstraint, string Name);
}
