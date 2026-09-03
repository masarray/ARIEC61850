namespace AR.Iec61850.Mms;

/// <summary>
/// Typed evidence from a fresh-association recovery of one exact G2.3 temporary
/// NamedVariableList. Recovery is intentionally narrower than qualification itself:
/// it may delete only when a fresh readable directory proves that the surviving list
/// contains the exact ordered members from the failed current-run attempt. A name match
/// alone never authorizes mutation, and an inspection exception never proves absence.
/// </summary>
public sealed class MmsDynamicDataSetQualificationRecoveryResult
{
    public bool IsSuccess { get; init; }
    public string DataSetReference { get; init; } = string.Empty;
    public IReadOnlyList<string> ExpectedMemberReferences { get; init; } = Array.Empty<string>();
    public bool NamePresentBefore { get; init; }
    public bool NamespaceAbsenceProvenBefore { get; init; }
    public bool DirectoryReadableBefore { get; init; }
    public bool DirectoryAbsenceProvenBefore { get; init; }
    public IReadOnlyList<string> ObservedMemberReferencesBefore { get; init; } = Array.Empty<string>();
    public bool ExactMembersVerifiedBeforeDelete { get; init; }
    public bool DeleteAttempted { get; init; }
    public MmsDeleteNamedVariableListResult? DeleteResult { get; init; }
    public bool NamePresentAfter { get; init; }
    public bool NamespaceAbsenceProvenAfter { get; init; }
    public bool DirectoryReadableAfter { get; init; }
    public bool DirectoryAbsenceProvenAfter { get; init; }
    public bool AssociationHealthy { get; init; }
    public string Failure { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
}

public static class MmsDynamicDataSetQualificationRecoveryPolicy
{
    public static bool ExactOrderedMembersMatch(
        IReadOnlyList<string> expected,
        IReadOnlyList<string> observed)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(observed);
        if (expected.Count != observed.Count)
            return false;

        for (var index = 0; index < expected.Count; index++)
        {
            if (!Normalize(expected[index]).Equals(Normalize(observed[index]), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public static bool CanDeleteExactResidue(
        bool namePresent,
        bool directoryReadable,
        IReadOnlyList<string> expectedMemberReferences,
        IReadOnlyList<string> observedMemberReferences,
        bool associationHealthy,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(expectedMemberReferences);
        ArgumentNullException.ThrowIfNull(observedMemberReferences);

        if (!associationHealthy)
        {
            reason = "Fresh MMS association is not healthy; residue delete is forbidden.";
            return false;
        }

        if (!namePresent && !directoryReadable)
        {
            reason = "No readable surviving qualification residue is available for exact delete proof.";
            return false;
        }

        if (!directoryReadable)
        {
            reason = "Qualification residue has no readable directory; a name match alone never authorizes delete.";
            return false;
        }

        if (expectedMemberReferences.Count == 0)
        {
            reason = "The failed qualification attempt has no exact expected member sequence; delete is forbidden.";
            return false;
        }

        if (!ExactOrderedMembersMatch(expectedMemberReferences, observedMemberReferences))
        {
            reason = "Fresh qualification residue directory does not exactly match the ordered members from the failed current-run attempt; delete is forbidden.";
            return false;
        }

        reason = "Fresh qualification residue has the exact ordered current-run member sequence; targeted delete of this exact temporary DataSet is permitted.";
        return true;
    }

    public static bool IsRecoveryClosed(
        bool namePresent,
        bool namespaceAbsenceProven,
        bool directoryReadable,
        bool directoryAbsenceProven,
        bool associationHealthy,
        out string reason)
    {
        if (!associationHealthy)
        {
            reason = "Fresh MMS association is not healthy after residue inspection/cleanup.";
            return false;
        }

        if (namePresent)
        {
            reason = "Temporary qualification DataSet is still advertised by fresh NamedVariableList discovery.";
            return false;
        }

        if (!namespaceAbsenceProven)
        {
            reason = "Temporary qualification DataSet namespace absence was not proven; missing/failed discovery is not absence evidence.";
            return false;
        }

        if (directoryReadable)
        {
            reason = "Temporary qualification DataSet still has a readable directory on the fresh association.";
            return false;
        }

        if (!directoryAbsenceProven)
        {
            reason = "Temporary qualification DataSet direct-directory absence was not proven; an exception is not absence evidence.";
            return false;
        }

        reason = "Fresh-association qualification cleanup is closed: temporary DataSet absent by fresh namespace and completed direct-directory checks, association healthy.";
        return true;
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().Replace('.', '$');
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// Recovers only the exact temporary NamedVariableList from a failed G2.3 attempt.
    /// Invoke this on a newly established MMS association. If the list is already absent,
    /// the operation is read-only. If it survives, DeleteNamedVariableList is allowed only
    /// after fresh directory readback exactly matches the failed attempt's ordered member
    /// sequence. Closure is then re-proven by successful namespace discovery, a completed
    /// direct-directory absence check, and a healthy association.
    /// </summary>
    public async Task<MmsDynamicDataSetQualificationRecoveryResult> RecoverDynamicDataSetQualificationResidueAsync(
        string dataSetReference,
        IReadOnlyList<string> expectedMemberReferences,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSetReference);
        ArgumentNullException.ThrowIfNull(expectedMemberReferences);
        if (expectedMemberReferences.Count == 0)
            throw new ArgumentException("Fresh qualification recovery requires the exact failed-attempt member sequence.", nameof(expectedMemberReferences));
        if (expectedMemberReferences.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Fresh qualification recovery member references cannot be empty.", nameof(expectedMemberReferences));
        if (expectedMemberReferences.Distinct(StringComparer.OrdinalIgnoreCase).Count() != expectedMemberReferences.Count)
            throw new ArgumentException("Fresh qualification recovery member references cannot contain duplicates.", nameof(expectedMemberReferences));

        var expected = expectedMemberReferences.Select(reference => reference.Trim()).ToArray();
        var evidence = new List<string>
        {
            $"G2.3 fresh recovery target: dataset={dataSetReference}; expectedMembers={expected.Length}; mutationPolicy=exact-current-run-residue-only"
        };

        var before = await InspectQualificationResidueAsync(dataSetReference, cancellationToken).ConfigureAwait(false);
        evidence.AddRange(before.EvidenceLines.Select(line => "G2.3 fresh recovery BEFORE: " + line));

        var initiallyClosed = MmsDynamicDataSetQualificationRecoveryPolicy.IsRecoveryClosed(
            before.NamePresent,
            before.NamespaceAbsenceProven,
            before.DirectoryReadable,
            before.DirectoryAbsenceProven,
            IsMmsInitiated,
            out var initialClosureReason);
        evidence.Add("G2.3 fresh recovery initial closure: " + initialClosureReason);
        if (initiallyClosed)
            return BuildResult(true, dataSetReference, expected, before, null, false, false, null, IsMmsInitiated,
                "G2.3 fresh recovery PASS: the temporary qualification DataSet was already proven absent on the fresh association; no delete mutation was required.", evidence);

        var mayDelete = MmsDynamicDataSetQualificationRecoveryPolicy.CanDeleteExactResidue(
            before.NamePresent,
            before.DirectoryReadable,
            expected,
            before.MemberReferences,
            IsMmsInitiated,
            out var deleteReason);
        evidence.Add("G2.3 fresh recovery delete gate: " + deleteReason);
        if (!mayDelete)
            return BuildResult(false, dataSetReference, expected, before, null, false, false, deleteReason, IsMmsInitiated,
                "G2.3 fresh recovery failed closed before delete. " + deleteReason, evidence);

        var delete = await SendQualificationRecoveryDeleteAsync(dataSetReference, cancellationToken).ConfigureAwait(false);
        evidence.Add($"G2.3 fresh recovery DELETE: attempted=true; success={delete.IsSuccess}; matched={delete.NumberMatched?.ToString() ?? "?"}; deleted={delete.NumberDeleted?.ToString() ?? "?"}; association={State}; result={delete.Message}");
        if (!delete.IsSuccess || !IsMmsInitiated)
        {
            var failure = !delete.IsSuccess
                ? "Exact temporary qualification DataSet delete was not accepted."
                : "MMS association was not healthy after exact temporary qualification DataSet delete.";
            return BuildResult(false, dataSetReference, expected, before, null, true, true, failure, IsMmsInitiated,
                "G2.3 fresh recovery failed closed after targeted delete. " + failure, evidence, delete);
        }

        var after = await InspectQualificationResidueAsync(dataSetReference, cancellationToken).ConfigureAwait(false);
        evidence.AddRange(after.EvidenceLines.Select(line => "G2.3 fresh recovery AFTER: " + line));
        var closed = MmsDynamicDataSetQualificationRecoveryPolicy.IsRecoveryClosed(
            after.NamePresent,
            after.NamespaceAbsenceProven,
            after.DirectoryReadable,
            after.DirectoryAbsenceProven,
            IsMmsInitiated,
            out var closureReason);
        evidence.Add("G2.3 fresh recovery final closure: " + closureReason);

        return BuildResult(
            closed,
            dataSetReference,
            expected,
            before,
            after,
            true,
            true,
            closed ? null : closureReason,
            IsMmsInitiated,
            closed
                ? "G2.3 fresh recovery PASS: exact current-run qualification residue was deleted and fresh namespace + direct-directory absence were proven on a healthy association."
                : "G2.3 fresh recovery did not prove complete cleanup closure after exact targeted delete. " + closureReason,
            evidence,
            delete);
    }

    private async Task<QualificationResidueInspection> InspectQualificationResidueAsync(
        string dataSetReference,
        CancellationToken cancellationToken)
    {
        var evidence = new List<string>();
        MmsDiscoveryResult discovery;
        try
        {
            discovery = await DiscoverAsync(
                probeReportAttributes: false,
                maxReportAttributeProbes: 0,
                cancellationToken: cancellationToken,
                readDataSetDirectories: false,
                maxDataSetDirectoryReads: 0).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            evidence.Add($"discovery exception={ex.GetType().Name}: {ex.Message}; association={State}");
            return new QualificationResidueInspection { EvidenceLines = evidence };
        }

        var namespaceState = InspectQualificationDataSetNamespace(discovery.Snapshot, dataSetReference);
        evidence.Add("namespace: " + namespaceState.Reason);

        MmsDataSetDirectoryResult directory;
        try
        {
            directory = await GetDataSetDirectoryAsync(dataSetReference, discovery.IedDirectory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            evidence.Add($"direct directory exception={ex.GetType().Name}: {ex.Message}; association={State}; absenceProven=false");
            return new QualificationResidueInspection
            {
                NamePresent = namespaceState.Present,
                NamespaceAbsenceProven = namespaceState.AbsenceProven,
                EvidenceLines = evidence
            };
        }

        var members = directory.Members.Select(member => member.MmsReference).ToArray();
        var directoryAbsenceProven = !directory.IsSuccess;
        evidence.Add($"direct directory: completed=true; readable={directory.IsSuccess}; absenceProven={directoryAbsenceProven}; members={members.Length}; association={State}; result={directory.Message}");
        if (directory.IsSuccess)
            evidence.Add("direct directory members: " + string.Join(" | ", members));

        return new QualificationResidueInspection
        {
            NamePresent = namespaceState.Present,
            NamespaceAbsenceProven = namespaceState.AbsenceProven,
            DirectoryReadable = directory.IsSuccess,
            DirectoryAbsenceProven = directoryAbsenceProven,
            MemberReferences = members,
            EvidenceLines = evidence
        };
    }

    private async Task<MmsDeleteNamedVariableListResult> SendQualificationRecoveryDeleteAsync(
        string dataSetReference,
        CancellationToken cancellationToken)
    {
        var invokeId = NextInvokeId();
        var request = MmsDeleteNamedVariableListRequest.Build(invokeId, dataSetReference);
        try
        {
            var response = await SendConfirmedPresentationPayloadAsync(request, invokeId, cancellationToken).ConfigureAwait(false);
            return MmsDeleteNamedVariableListResponseDecoder.Decode(response, invokeId, dataSetReference);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ObjectDisposedException)
        {
            await MarkProtocolFaultAsync().ConfigureAwait(false);
            return new MmsDeleteNamedVariableListResult
            {
                IsSuccess = false,
                DataSetReference = dataSetReference,
                Message = $"G2.3 fresh recovery DeleteNamedVariableList transport fault: {ex.GetType().Name}: {ex.Message}"
            };
        }
    }

    private static QualificationNamespaceState InspectQualificationDataSetNamespace(
        MmsDiscoverySnapshot snapshot,
        string dataSetReference)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var (domain, itemName) = MmsDataSetDirectoryRequest.ParseDataSetReference(dataSetReference);
        var normalizedItem = itemName.Trim().Replace('.', '$');
        if (!snapshot.DomainVariableLists.TryGetValue(domain, out var names))
        {
            return new QualificationNamespaceState
            {
                Present = false,
                AbsenceProven = false,
                Reason = $"domain {domain} is absent from NamedVariableList discovery; namespace absence is not proven."
            };
        }

        var present = names.Any(name =>
            (name ?? string.Empty).Trim().Replace('.', '$').Equals(normalizedItem, StringComparison.OrdinalIgnoreCase));
        return new QualificationNamespaceState
        {
            Present = present,
            AbsenceProven = !present,
            Reason = present
                ? $"temporary qualification DataSet is advertised: domain={domain}; item={normalizedItem}."
                : $"temporary qualification DataSet absence is proven by NamedVariableList discovery: domain={domain}; item={normalizedItem}; advertisedLists={names.Count}."
        };
    }

    private static MmsDynamicDataSetQualificationRecoveryResult BuildResult(
        bool success,
        string dataSetReference,
        IReadOnlyList<string> expected,
        QualificationResidueInspection before,
        QualificationResidueInspection? after,
        bool exactMembers,
        bool deleteAttempted,
        string? failure,
        bool associationHealthy,
        string summary,
        IReadOnlyList<string> evidence,
        MmsDeleteNamedVariableListResult? delete = null)
        => new()
        {
            IsSuccess = success,
            DataSetReference = dataSetReference,
            ExpectedMemberReferences = expected.ToArray(),
            NamePresentBefore = before.NamePresent,
            NamespaceAbsenceProvenBefore = before.NamespaceAbsenceProven,
            DirectoryReadableBefore = before.DirectoryReadable,
            DirectoryAbsenceProvenBefore = before.DirectoryAbsenceProven,
            ObservedMemberReferencesBefore = before.MemberReferences.ToArray(),
            ExactMembersVerifiedBeforeDelete = exactMembers,
            DeleteAttempted = deleteAttempted,
            DeleteResult = delete,
            NamePresentAfter = after?.NamePresent ?? false,
            NamespaceAbsenceProvenAfter = after?.NamespaceAbsenceProven ?? false,
            DirectoryReadableAfter = after?.DirectoryReadable ?? false,
            DirectoryAbsenceProvenAfter = after?.DirectoryAbsenceProven ?? false,
            AssociationHealthy = associationHealthy,
            Failure = failure ?? string.Empty,
            Summary = summary,
            EvidenceLines = evidence.ToArray()
        };

    private sealed class QualificationResidueInspection
    {
        public bool NamePresent { get; init; }
        public bool NamespaceAbsenceProven { get; init; }
        public bool DirectoryReadable { get; init; }
        public bool DirectoryAbsenceProven { get; init; }
        public IReadOnlyList<string> MemberReferences { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
    }

    private sealed class QualificationNamespaceState
    {
        public bool Present { get; init; }
        public bool AbsenceProven { get; init; }
        public string Reason { get; init; } = string.Empty;
    }
}
