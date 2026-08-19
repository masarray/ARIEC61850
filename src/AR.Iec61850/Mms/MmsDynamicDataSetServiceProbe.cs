using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

public enum MmsDynamicDataSetProbeFailureStage
{
    None,
    DefineNamedVariableList,
    GetNamedVariableListAttributes,
    DeleteNamedVariableList
}

public sealed class MmsDynamicDataSetProbeServiceEvidence
{
    public string Service { get; init; } = string.Empty;
    public bool Attempted { get; init; }
    public bool IsSuccess { get; init; }
    public int InvokeId { get; init; }
    public string DataSetReference { get; init; } = string.Empty;
    public string MemberReference { get; init; } = string.Empty;
    public string RequestHex { get; init; } = string.Empty;
    public string ResponseHex { get; init; } = string.Empty;
    public MmsAssociationState StateBefore { get; init; }
    public MmsAssociationState StateAfter { get; init; }
    public string ReceiveRoutingSummary { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public string Summary =>
        $"{Service}: success={IsSuccess}, invokeID={InvokeId}, state={StateBefore}->{StateAfter}, dataset={DataSetReference}, member={MemberReference}, route={TextOrDash(ReceiveRoutingSummary)}, result={TextOrDash(Message)}";

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}

public sealed class MmsDynamicDataSetProbeResult
{
    public bool IsSuccess { get; init; }
    public MmsDynamicDataSetProbeFailureStage FailureStage { get; init; }
    public string DataSetReference { get; init; } = string.Empty;
    public string MemberReference { get; init; } = string.Empty;
    public MmsDynamicDataSetProbeServiceEvidence DefineEvidence { get; init; } = new();
    public bool DirectoryAttempted { get; init; }
    public bool DirectoryVerified { get; init; }
    public string DirectoryMessage { get; init; } = string.Empty;
    public string DirectoryResponseHex { get; init; } = string.Empty;
    public MmsDynamicDataSetProbeServiceEvidence DeleteEvidence { get; init; } = new();
    public IReadOnlyList<MmsReportAttributeWriteStep> WriteSteps { get; init; } = Array.Empty<MmsReportAttributeWriteStep>();
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();

    public bool DynamicMutationAttempted => DefineEvidence.Attempted;
    public bool CleanupAttempted => DeleteEvidence.Attempted;
    // If Define succeeded, the temporary NamedVariableList may exist on the server.
    // Cleanup is therefore successful only when Delete was actually attempted and accepted.
    // A failed Define needs no cleanup because the list was never proven created.
    public bool CleanupSucceeded => !DefineEvidence.IsSuccess || (CleanupAttempted && DeleteEvidence.IsSuccess);
    // If verification was attempted but Delete could not even be issued, the association
    // was lost between Define and cleanup. Do not report that path as association-survived.
    public bool AssociationSurvived => DefineEvidence.StateAfter == MmsAssociationState.MmsInitiated &&
                                       (DeleteEvidence.Attempted
                                           ? DeleteEvidence.StateAfter == MmsAssociationState.MmsInitiated
                                           : !DirectoryAttempted);

    public string Summary => IsSuccess
        ? $"single-member NVL probe succeeded for {DataSetReference}; member={MemberReference}; define/verify/delete all completed on one MMS association."
        : $"single-member NVL probe failed at {FailureStage} for {DataSetReference}; member={MemberReference}; associationSurvived={AssociationSurvived}.";
}

public static class MmsDynamicDataSetProbePolicy
{
    public static bool ShouldProbe(MmsReportSubscriptionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Mode == MmsReportSubscriptionPlanMode.DynamicDataSet &&
               plan.IsReady &&
               plan.ReportControl is not null &&
               !string.IsNullOrWhiteSpace(plan.DataSetReference) &&
               plan.DynamicPoints.Count > 0;
    }

    public static MmsReportActivationFailureReason FailureReason(MmsDynamicDataSetProbeFailureStage stage)
        => stage switch
        {
            MmsDynamicDataSetProbeFailureStage.DefineNamedVariableList => MmsReportActivationFailureReason.DynamicDataSetProbeDefineFailed,
            MmsDynamicDataSetProbeFailureStage.GetNamedVariableListAttributes => MmsReportActivationFailureReason.DynamicDataSetProbeVerificationFailed,
            MmsDynamicDataSetProbeFailureStage.DeleteNamedVariableList => MmsReportActivationFailureReason.DynamicDataSetProbeDeleteFailed,
            _ => MmsReportActivationFailureReason.OtherActivationFailure
        };
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// P6.2 field probe for dynamic DataSet service interoperability. It uses the exact
    /// planned DataSet reference but only one already-resolved member, verifies the list
    /// through GetNamedVariableListAttributes, then deletes it before any RCB attribute is
    /// mutated. A relay that aborts on DefineNamedVariableList therefore fails before a
    /// static or dynamic RCB is armed, while the returned evidence preserves the exact
    /// invokeID, request/response BER, routing result, and association state transition.
    /// </summary>
    public async Task<MmsDynamicDataSetProbeResult> ProbeDynamicDataSetServiceAsync(
        string dataSetReference,
        MmsObjectReference member,
        MmsIedModelDirectory? directory = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSetReference);
        if (string.IsNullOrWhiteSpace(member.Domain) || string.IsNullOrWhiteSpace(member.Item))
            throw new ArgumentException("Dynamic DataSet probe requires one resolved MMS member.", nameof(member));

        var memberReference = $"{member.Domain}/{member.Item}";
        var steps = new List<MmsReportAttributeWriteStep>();
        var evidence = new List<string>();

        var define = await SendProbeDefineAsync(dataSetReference, member, cancellationToken).ConfigureAwait(false);
        steps.Add(new MmsReportAttributeWriteStep
        {
            Attribute = "Probe.DefineNamedVariableList",
            Reference = dataSetReference,
            Attempted = true,
            IsSuccess = define.IsSuccess,
            Message = define.Message
        });
        AppendServiceEvidence(evidence, "DEFINE", define);

        if (!define.IsSuccess || !IsMmsInitiated)
        {
            return BuildProbeResult(
                false,
                MmsDynamicDataSetProbeFailureStage.DefineNamedVariableList,
                dataSetReference,
                memberReference,
                define,
                false,
                false,
                string.Empty,
                string.Empty,
                new MmsDynamicDataSetProbeServiceEvidence(),
                steps,
                evidence);
        }

        MmsDataSetDirectoryResult directoryResult;
        try
        {
            directoryResult = await GetDataSetDirectoryAsync(dataSetReference, directory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            directoryResult = new MmsDataSetDirectoryResult
            {
                IsSuccess = false,
                DataSetReference = dataSetReference,
                Message = $"Probe DataSet directory exception: {ex.GetType().Name}: {ex.Message}"
            };
        }

        var directoryVerified = directoryResult.IsSuccess &&
                                directoryResult.Members.Count == 1 &&
                                directoryResult.Members.Any(candidate =>
                                    candidate.MmsReference.Equals(memberReference, StringComparison.OrdinalIgnoreCase));
        steps.Add(new MmsReportAttributeWriteStep
        {
            Attribute = "Probe.GetNamedVariableListAttributes",
            Reference = dataSetReference,
            Attempted = true,
            IsSuccess = directoryVerified,
            Message = directoryResult.IsSuccess
                ? $"Directory returned {directoryResult.Members.Count} member(s); exactSingleMember={directoryVerified}. {directoryResult.Message}"
                : directoryResult.Message
        });
        evidence.Add(
            $"P6.2 NVL PROBE VERIFY: success={directoryVerified}; dataset={dataSetReference}; expectedMember={memberReference}; returnedMembers={directoryResult.Members.Count}; result={directoryResult.Message}; responseHEX={TextOrNone(directoryResult.ResponseHexPreview)}");

        var delete = IsMmsInitiated
            ? await SendProbeDeleteAsync(dataSetReference, memberReference, cancellationToken).ConfigureAwait(false)
            : new MmsDynamicDataSetProbeServiceEvidence
            {
                Service = "DeleteNamedVariableList",
                Attempted = false,
                IsSuccess = false,
                DataSetReference = dataSetReference,
                MemberReference = memberReference,
                StateBefore = State,
                StateAfter = State,
                Message = "Delete probe was not attempted because the MMS association was no longer initiated."
            };

        if (delete.Attempted)
        {
            steps.Add(new MmsReportAttributeWriteStep
            {
                Attribute = "Probe.DeleteNamedVariableList",
                Reference = dataSetReference,
                Attempted = true,
                IsSuccess = delete.IsSuccess,
                Message = delete.Message
            });
            AppendServiceEvidence(evidence, "DELETE", delete);
        }
        else
        {
            evidence.Add($"P6.2 NVL PROBE DELETE: skipped; state={State}; dataset={dataSetReference}; reason={delete.Message}");
        }

        if (!directoryVerified)
        {
            return BuildProbeResult(
                false,
                MmsDynamicDataSetProbeFailureStage.GetNamedVariableListAttributes,
                dataSetReference,
                memberReference,
                define,
                true,
                false,
                directoryResult.Message,
                directoryResult.ResponseHexPreview,
                delete,
                steps,
                evidence);
        }

        if (!delete.IsSuccess)
        {
            return BuildProbeResult(
                false,
                MmsDynamicDataSetProbeFailureStage.DeleteNamedVariableList,
                dataSetReference,
                memberReference,
                define,
                true,
                true,
                directoryResult.Message,
                directoryResult.ResponseHexPreview,
                delete,
                steps,
                evidence);
        }

        return BuildProbeResult(
            true,
            MmsDynamicDataSetProbeFailureStage.None,
            dataSetReference,
            memberReference,
            define,
            true,
            true,
            directoryResult.Message,
            directoryResult.ResponseHexPreview,
            delete,
            steps,
            evidence);
    }

    private async Task<MmsDynamicDataSetProbeServiceEvidence> SendProbeDefineAsync(
        string dataSetReference,
        MmsObjectReference member,
        CancellationToken cancellationToken)
    {
        var stateBefore = State;
        var invokeId = NextInvokeId();
        var request = MmsDefineNamedVariableListRequest.Build(invokeId, dataSetReference, [member]);
        var requestHex = HexDump.ToCompactString(request);
        LastDiscoveryRequestHex = requestHex;
        LastDiscoveryResponseHex = string.Empty;
        LastReceiveRoutingSummary = string.Empty;

        try
        {
            var response = await SendConfirmedPresentationPayloadAsync(request, invokeId, cancellationToken).ConfigureAwait(false);
            var result = MmsDefineNamedVariableListResponseDecoder.Decode(response, invokeId, dataSetReference);
            LastDiscoveryResponseHex = result.ResponseHexPreview;
            LastDiscoveryAttemptSummary = result.Message;
            return new MmsDynamicDataSetProbeServiceEvidence
            {
                Service = "DefineNamedVariableList",
                Attempted = true,
                IsSuccess = result.IsSuccess,
                InvokeId = invokeId,
                DataSetReference = dataSetReference,
                MemberReference = $"{member.Domain}/{member.Item}",
                RequestHex = requestHex,
                ResponseHex = result.ResponseHexPreview,
                StateBefore = stateBefore,
                StateAfter = State,
                ReceiveRoutingSummary = LastReceiveRoutingSummary,
                Message = result.Message
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            var route = LastReceiveRoutingSummary;
            var responseHex = LastDiscoveryResponseHex;
            var message = $"DefineNamedVariableList probe transport fault: {ex.GetType().Name}: {ex.Message}";
            await MarkProtocolFaultAsync().ConfigureAwait(false);
            LastDiscoveryAttemptSummary = message;
            return new MmsDynamicDataSetProbeServiceEvidence
            {
                Service = "DefineNamedVariableList",
                Attempted = true,
                IsSuccess = false,
                InvokeId = invokeId,
                DataSetReference = dataSetReference,
                MemberReference = $"{member.Domain}/{member.Item}",
                RequestHex = requestHex,
                ResponseHex = responseHex,
                StateBefore = stateBefore,
                StateAfter = State,
                ReceiveRoutingSummary = route,
                Message = message
            };
        }
    }

    private async Task<MmsDynamicDataSetProbeServiceEvidence> SendProbeDeleteAsync(
        string dataSetReference,
        string memberReference,
        CancellationToken cancellationToken)
    {
        var stateBefore = State;
        var invokeId = NextInvokeId();
        var request = MmsDeleteNamedVariableListRequest.Build(invokeId, dataSetReference);
        var requestHex = HexDump.ToCompactString(request);
        LastDiscoveryRequestHex = requestHex;
        LastDiscoveryResponseHex = string.Empty;
        LastReceiveRoutingSummary = string.Empty;

        try
        {
            var response = await SendConfirmedPresentationPayloadAsync(request, invokeId, cancellationToken).ConfigureAwait(false);
            var result = MmsDeleteNamedVariableListResponseDecoder.Decode(response, invokeId, dataSetReference);
            LastDiscoveryResponseHex = result.ResponseHexPreview;
            LastDiscoveryAttemptSummary = result.Message;
            return new MmsDynamicDataSetProbeServiceEvidence
            {
                Service = "DeleteNamedVariableList",
                Attempted = true,
                IsSuccess = result.IsSuccess,
                InvokeId = invokeId,
                DataSetReference = dataSetReference,
                MemberReference = memberReference,
                RequestHex = requestHex,
                ResponseHex = result.ResponseHexPreview,
                StateBefore = stateBefore,
                StateAfter = State,
                ReceiveRoutingSummary = LastReceiveRoutingSummary,
                Message = result.Message
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            var route = LastReceiveRoutingSummary;
            var responseHex = LastDiscoveryResponseHex;
            var message = $"DeleteNamedVariableList probe transport fault: {ex.GetType().Name}: {ex.Message}";
            await MarkProtocolFaultAsync().ConfigureAwait(false);
            LastDiscoveryAttemptSummary = message;
            return new MmsDynamicDataSetProbeServiceEvidence
            {
                Service = "DeleteNamedVariableList",
                Attempted = true,
                IsSuccess = false,
                InvokeId = invokeId,
                DataSetReference = dataSetReference,
                MemberReference = memberReference,
                RequestHex = requestHex,
                ResponseHex = responseHex,
                StateBefore = stateBefore,
                StateAfter = State,
                ReceiveRoutingSummary = route,
                Message = message
            };
        }
    }

    private static MmsDynamicDataSetProbeResult BuildProbeResult(
        bool isSuccess,
        MmsDynamicDataSetProbeFailureStage failureStage,
        string dataSetReference,
        string memberReference,
        MmsDynamicDataSetProbeServiceEvidence define,
        bool directoryAttempted,
        bool directoryVerified,
        string directoryMessage,
        string directoryResponseHex,
        MmsDynamicDataSetProbeServiceEvidence delete,
        IReadOnlyList<MmsReportAttributeWriteStep> steps,
        IReadOnlyList<string> evidence)
        => new()
        {
            IsSuccess = isSuccess,
            FailureStage = failureStage,
            DataSetReference = dataSetReference,
            MemberReference = memberReference,
            DefineEvidence = define,
            DirectoryAttempted = directoryAttempted,
            DirectoryVerified = directoryVerified,
            DirectoryMessage = directoryMessage,
            DirectoryResponseHex = directoryResponseHex,
            DeleteEvidence = delete,
            WriteSteps = steps.ToArray(),
            EvidenceLines = evidence.ToArray()
        };

    private static void AppendServiceEvidence(
        ICollection<string> lines,
        string label,
        MmsDynamicDataSetProbeServiceEvidence evidence)
    {
        lines.Add($"P6.2 NVL PROBE {label}: {evidence.Summary}");
        lines.Add($"P6.2 NVL PROBE {label} requestHEX={TextOrNone(evidence.RequestHex)}");
        lines.Add($"P6.2 NVL PROBE {label} responseHEX={TextOrNone(evidence.ResponseHex)}");
    }

    private static string TextOrNone(string? value)
        => string.IsNullOrWhiteSpace(value) ? "<none>" : value.Trim();
}
