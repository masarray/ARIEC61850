using AR.Iec61850.Diagnostics;

namespace AR.Iec61850.Mms;

/// <summary>
/// G2 qualification is intentionally separate from the production persistent-report path.
/// These stages describe a temporary NamedVariableList qualification transaction only;
/// no Report Control Block is mutated by this API.
/// </summary>
public enum MmsDynamicDataSetQualificationFailureStage
{
    None,
    Preflight,
    DefineNamedVariableList,
    GetNamedVariableListAttributes,
    DeleteNamedVariableList
}

/// <summary>
/// Application-side safety bounds for an explicit commissioning/qualification probe.
/// This limit is not an IED capability claim. G2.2 will discover the actual safe envelope
/// from bounded evidence instead of assuming this value is the relay limit.
/// </summary>
public sealed class MmsDynamicDataSetQualificationProbeOptions
{
    public const int DefaultApplicationSafetyMemberLimit = 64;
    public const int AbsoluteApplicationSafetyMemberLimit = 256;

    public int ApplicationSafetyMemberLimit { get; init; } = DefaultApplicationSafetyMemberLimit;
    public bool RejectKnownNegotiatedPduOverflow { get; init; } = true;
}

public sealed class MmsDynamicDataSetQualificationServiceEvidence
{
    public string Service { get; init; } = string.Empty;
    public bool Attempted { get; init; }
    public bool IsSuccess { get; init; }
    public int InvokeId { get; init; }
    public string DataSetReference { get; init; } = string.Empty;
    public IReadOnlyList<string> MemberReferences { get; init; } = Array.Empty<string>();
    public int RequestByteCount { get; init; }
    public int? NegotiatedMaxMmsPduSize { get; init; }
    public string RequestHex { get; init; } = string.Empty;
    public string ResponseHex { get; init; } = string.Empty;
    public MmsAssociationState StateBefore { get; init; }
    public MmsAssociationState StateAfter { get; init; }
    public string ReceiveRoutingSummary { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public int MemberCount => MemberReferences.Count;

    public string Summary =>
        $"{Service}: attempted={Attempted}, success={IsSuccess}, invokeID={InvokeId}, " +
        $"state={StateBefore}->{StateAfter}, dataset={DataSetReference}, members={MemberCount}, " +
        $"requestBytes={RequestByteCount}, negotiatedMaxPdu={NegotiatedMaxMmsPduSize?.ToString() ?? "?"}, " +
        $"route={TextOrDash(ReceiveRoutingSummary)}, result={TextOrDash(Message)}";

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}

public sealed class MmsDynamicDataSetQualificationProbeResult
{
    public bool IsSuccess { get; init; }
    public MmsDynamicDataSetQualificationFailureStage FailureStage { get; init; }
    public string DataSetReference { get; init; } = string.Empty;
    public IReadOnlyList<string> RequestedMemberReferences { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ReturnedMemberReferences { get; init; } = Array.Empty<string>();
    public int ApplicationSafetyMemberLimit { get; init; }
    public int DefineRequestByteCount { get; init; }
    public int? NegotiatedMaxMmsPduSize { get; init; }
    public bool RequestWithinKnownNegotiatedPdu { get; init; } = true;
    public MmsDynamicDataSetQualificationServiceEvidence DefineEvidence { get; init; } = new();
    public bool DirectoryAttempted { get; init; }
    public bool DirectoryVerified { get; init; }
    public string DirectoryMessage { get; init; } = string.Empty;
    public string DirectoryResponseHex { get; init; } = string.Empty;
    public MmsDynamicDataSetQualificationServiceEvidence DeleteEvidence { get; init; } = new();
    public IReadOnlyList<MmsReportAttributeWriteStep> WriteSteps { get; init; } = Array.Empty<MmsReportAttributeWriteStep>();
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();

    public int RequestedMemberCount => RequestedMemberReferences.Count;
    public int ReturnedMemberCount => ReturnedMemberReferences.Count;
    public bool DynamicMutationAttempted => DefineEvidence.Attempted;
    public bool CleanupAttempted => DeleteEvidence.Attempted;

    // If Define succeeded, the temporary list may exist on the server. Qualification is
    // cleanup-safe only when Delete was actually attempted and accepted.
    public bool CleanupSucceeded => !DefineEvidence.IsSuccess || (CleanupAttempted && DeleteEvidence.IsSuccess);

    public bool AssociationSurvived => DefineEvidence.StateAfter == MmsAssociationState.MmsInitiated &&
                                       (DeleteEvidence.Attempted
                                           ? DeleteEvidence.StateAfter == MmsAssociationState.MmsInitiated
                                           : !DirectoryAttempted);

    public string Summary => IsSuccess
        ? $"G2.1 dynamic DataSet qualification succeeded for {DataSetReference}; members={RequestedMemberCount}; " +
          $"requestBytes={DefineRequestByteCount}; maxPdu={NegotiatedMaxMmsPduSize?.ToString() ?? "?"}; " +
          "define/verify/delete completed on one MMS association."
        : $"G2.1 dynamic DataSet qualification failed at {FailureStage} for {DataSetReference}; " +
          $"members={RequestedMemberCount}; requestBytes={DefineRequestByteCount}; " +
          $"maxPdu={NegotiatedMaxMmsPduSize?.ToString() ?? "?"}; associationSurvived={AssociationSurvived}.";
}

public static class MmsDynamicDataSetQualificationPolicy
{
    public static bool ExactOrderedMembersMatch(
        IReadOnlyList<string> expected,
        IReadOnlyList<string> returned)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(returned);

        if (expected.Count != returned.Count)
            return false;

        for (var index = 0; index < expected.Count; index++)
        {
            if (!expected[index].Equals(returned[index], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public static bool IsWithinKnownNegotiatedPdu(int requestByteCount, int? negotiatedMaxMmsPduSize)
    {
        if (requestByteCount < 0)
            throw new ArgumentOutOfRangeException(nameof(requestByteCount));

        return !negotiatedMaxMmsPduSize.HasValue ||
               negotiatedMaxMmsPduSize.Value <= 0 ||
               requestByteCount <= negotiatedMaxMmsPduSize.Value;
    }

    public static void ValidateOptions(MmsDynamicDataSetQualificationProbeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ApplicationSafetyMemberLimit < 1 ||
            options.ApplicationSafetyMemberLimit > MmsDynamicDataSetQualificationProbeOptions.AbsoluteApplicationSafetyMemberLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"ApplicationSafetyMemberLimit must be between 1 and {MmsDynamicDataSetQualificationProbeOptions.AbsoluteApplicationSafetyMemberLimit}.");
        }
    }
}

public sealed partial class MmsClientSession
{
    /// <summary>
    /// G2.1 qualification-only probe for a bounded exact member set. The transaction is:
    /// DefineNamedVariableList -> GetNamedVariableListAttributes -> DeleteNamedVariableList.
    /// It never binds or enables an RCB. The production P6.2 single-member probation and
    /// automatic dynamic-report quarantine remain unchanged.
    /// </summary>
    public async Task<MmsDynamicDataSetQualificationProbeResult> ProbeDynamicDataSetQualificationAsync(
        string dataSetReference,
        IReadOnlyList<MmsObjectReference> members,
        MmsDynamicDataSetQualificationProbeOptions? options = null,
        MmsIedModelDirectory? directory = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSetReference);
        ArgumentNullException.ThrowIfNull(members);

        options ??= new MmsDynamicDataSetQualificationProbeOptions();
        MmsDynamicDataSetQualificationPolicy.ValidateOptions(options);

        if (members.Count == 0)
            throw new ArgumentException("Dynamic DataSet qualification requires at least one resolved MMS member.", nameof(members));

        var memberReferences = members
            .Select(ToQualificationMemberReference)
            .ToArray();

        var duplicate = memberReferences
            .GroupBy(reference => reference, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Dynamic DataSet qualification member is duplicated: {duplicate.Key}", nameof(members));

        var evidence = new List<string>();
        var steps = new List<MmsReportAttributeWriteStep>();
        var negotiatedMaxPdu = LastNegotiatedCapabilities.MaxMmsPduSize;

        if (members.Count > options.ApplicationSafetyMemberLimit)
        {
            var message =
                $"Qualification member count {members.Count} exceeds application safety limit {options.ApplicationSafetyMemberLimit}; no MMS mutation was attempted.";
            evidence.Add(
                $"G2.1 NVL QUALIFY PREFLIGHT: success=false; dataset={dataSetReference}; members={members.Count}; " +
                $"applicationSafetyLimit={options.ApplicationSafetyMemberLimit}; negotiatedMaxPdu={negotiatedMaxPdu?.ToString() ?? "?"}; result={message}");

            return BuildQualificationResult(
                false,
                MmsDynamicDataSetQualificationFailureStage.Preflight,
                dataSetReference,
                memberReferences,
                Array.Empty<string>(),
                options.ApplicationSafetyMemberLimit,
                0,
                negotiatedMaxPdu,
                true,
                new MmsDynamicDataSetQualificationServiceEvidence
                {
                    Service = "DefineNamedVariableList",
                    Attempted = false,
                    IsSuccess = false,
                    DataSetReference = dataSetReference,
                    MemberReferences = memberReferences,
                    NegotiatedMaxMmsPduSize = negotiatedMaxPdu,
                    StateBefore = State,
                    StateAfter = State,
                    Message = message
                },
                false,
                false,
                string.Empty,
                string.Empty,
                new MmsDynamicDataSetQualificationServiceEvidence(),
                steps,
                evidence);
        }

        var stateBeforeDefine = State;
        var invokeId = NextInvokeId();
        var defineRequest = MmsDefineNamedVariableListRequest.Build(invokeId, dataSetReference, members);
        var defineRequestHex = HexDump.ToCompactString(defineRequest);
        var requestWithinPdu = MmsDynamicDataSetQualificationPolicy.IsWithinKnownNegotiatedPdu(
            defineRequest.Length,
            negotiatedMaxPdu);

        if (options.RejectKnownNegotiatedPduOverflow && !requestWithinPdu)
        {
            var message =
                $"Encoded DefineNamedVariableList request is {defineRequest.Length} byte(s), exceeding negotiated MMS max PDU {negotiatedMaxPdu}; no request was sent.";
            var preflight = new MmsDynamicDataSetQualificationServiceEvidence
            {
                Service = "DefineNamedVariableList",
                Attempted = false,
                IsSuccess = false,
                InvokeId = invokeId,
                DataSetReference = dataSetReference,
                MemberReferences = memberReferences,
                RequestByteCount = defineRequest.Length,
                NegotiatedMaxMmsPduSize = negotiatedMaxPdu,
                RequestHex = defineRequestHex,
                StateBefore = stateBeforeDefine,
                StateAfter = State,
                Message = message
            };
            AppendQualificationServiceEvidence(evidence, "PREFLIGHT", preflight);

            return BuildQualificationResult(
                false,
                MmsDynamicDataSetQualificationFailureStage.Preflight,
                dataSetReference,
                memberReferences,
                Array.Empty<string>(),
                options.ApplicationSafetyMemberLimit,
                defineRequest.Length,
                negotiatedMaxPdu,
                false,
                preflight,
                false,
                false,
                string.Empty,
                string.Empty,
                new MmsDynamicDataSetQualificationServiceEvidence(),
                steps,
                evidence);
        }

        var define = await SendQualificationDefineAsync(
            dataSetReference,
            memberReferences,
            defineRequest,
            defineRequestHex,
            invokeId,
            stateBeforeDefine,
            negotiatedMaxPdu,
            cancellationToken).ConfigureAwait(false);

        steps.Add(new MmsReportAttributeWriteStep
        {
            Attribute = "Qualification.DefineNamedVariableList",
            Reference = dataSetReference,
            Attempted = define.Attempted,
            IsSuccess = define.IsSuccess,
            Message = define.Message
        });
        AppendQualificationServiceEvidence(evidence, "DEFINE", define);

        if (!define.IsSuccess || !IsMmsInitiated)
        {
            return BuildQualificationResult(
                false,
                MmsDynamicDataSetQualificationFailureStage.DefineNamedVariableList,
                dataSetReference,
                memberReferences,
                Array.Empty<string>(),
                options.ApplicationSafetyMemberLimit,
                defineRequest.Length,
                negotiatedMaxPdu,
                requestWithinPdu,
                define,
                false,
                false,
                string.Empty,
                string.Empty,
                new MmsDynamicDataSetQualificationServiceEvidence(),
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
                Message = $"G2.1 qualification DataSet directory exception: {ex.GetType().Name}: {ex.Message}"
            };
        }

        var returnedMemberReferences = directoryResult.Members
            .Select(candidate => candidate.MmsReference)
            .ToArray();
        var directoryVerified = directoryResult.IsSuccess &&
                                MmsDynamicDataSetQualificationPolicy.ExactOrderedMembersMatch(
                                    memberReferences,
                                    returnedMemberReferences);

        steps.Add(new MmsReportAttributeWriteStep
        {
            Attribute = "Qualification.GetNamedVariableListAttributes",
            Reference = dataSetReference,
            Attempted = true,
            IsSuccess = directoryVerified,
            Message = directoryResult.IsSuccess
                ? $"Directory returned {returnedMemberReferences.Length} member(s); exactOrderedMembers={directoryVerified}. {directoryResult.Message}"
                : directoryResult.Message
        });
        evidence.Add(
            $"G2.1 NVL QUALIFY VERIFY: success={directoryVerified}; dataset={dataSetReference}; " +
            $"expectedCount={memberReferences.Length}; returnedCount={returnedMemberReferences.Length}; " +
            $"expectedMembers={FormatMembers(memberReferences)}; returnedMembers={FormatMembers(returnedMemberReferences)}; " +
            $"result={directoryResult.Message}; responseHEX={QualificationTextOrNone(directoryResult.ResponseHexPreview)}");

        var delete = IsMmsInitiated
            ? await SendQualificationDeleteAsync(
                dataSetReference,
                memberReferences,
                negotiatedMaxPdu,
                cancellationToken).ConfigureAwait(false)
            : new MmsDynamicDataSetQualificationServiceEvidence
            {
                Service = "DeleteNamedVariableList",
                Attempted = false,
                IsSuccess = false,
                DataSetReference = dataSetReference,
                MemberReferences = memberReferences,
                NegotiatedMaxMmsPduSize = negotiatedMaxPdu,
                StateBefore = State,
                StateAfter = State,
                Message = "Qualification cleanup DeleteNamedVariableList was not attempted because the MMS association was no longer initiated."
            };

        if (delete.Attempted)
        {
            steps.Add(new MmsReportAttributeWriteStep
            {
                Attribute = "Qualification.DeleteNamedVariableList",
                Reference = dataSetReference,
                Attempted = true,
                IsSuccess = delete.IsSuccess,
                Message = delete.Message
            });
            AppendQualificationServiceEvidence(evidence, "DELETE", delete);
        }
        else
        {
            evidence.Add(
                $"G2.1 NVL QUALIFY DELETE: skipped; state={State}; dataset={dataSetReference}; reason={delete.Message}");
        }

        if (!directoryVerified)
        {
            return BuildQualificationResult(
                false,
                MmsDynamicDataSetQualificationFailureStage.GetNamedVariableListAttributes,
                dataSetReference,
                memberReferences,
                returnedMemberReferences,
                options.ApplicationSafetyMemberLimit,
                defineRequest.Length,
                negotiatedMaxPdu,
                requestWithinPdu,
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
            return BuildQualificationResult(
                false,
                MmsDynamicDataSetQualificationFailureStage.DeleteNamedVariableList,
                dataSetReference,
                memberReferences,
                returnedMemberReferences,
                options.ApplicationSafetyMemberLimit,
                defineRequest.Length,
                negotiatedMaxPdu,
                requestWithinPdu,
                define,
                true,
                true,
                directoryResult.Message,
                directoryResult.ResponseHexPreview,
                delete,
                steps,
                evidence);
        }

        return BuildQualificationResult(
            true,
            MmsDynamicDataSetQualificationFailureStage.None,
            dataSetReference,
            memberReferences,
            returnedMemberReferences,
            options.ApplicationSafetyMemberLimit,
            defineRequest.Length,
            negotiatedMaxPdu,
            requestWithinPdu,
            define,
            true,
            true,
            directoryResult.Message,
            directoryResult.ResponseHexPreview,
            delete,
            steps,
            evidence);
    }

    private async Task<MmsDynamicDataSetQualificationServiceEvidence> SendQualificationDefineAsync(
        string dataSetReference,
        IReadOnlyList<string> memberReferences,
        byte[] request,
        string requestHex,
        int invokeId,
        MmsAssociationState stateBefore,
        int? negotiatedMaxPdu,
        CancellationToken cancellationToken)
    {
        LastDiscoveryRequestHex = requestHex;
        LastDiscoveryResponseHex = string.Empty;
        LastReceiveRoutingSummary = string.Empty;

        try
        {
            var response = await SendConfirmedPresentationPayloadAsync(request, invokeId, cancellationToken).ConfigureAwait(false);
            var result = MmsDefineNamedVariableListResponseDecoder.Decode(response, invokeId, dataSetReference);
            LastDiscoveryResponseHex = result.ResponseHexPreview;
            LastDiscoveryAttemptSummary = result.Message;
            return new MmsDynamicDataSetQualificationServiceEvidence
            {
                Service = "DefineNamedVariableList",
                Attempted = true,
                IsSuccess = result.IsSuccess,
                InvokeId = invokeId,
                DataSetReference = dataSetReference,
                MemberReferences = memberReferences.ToArray(),
                RequestByteCount = request.Length,
                NegotiatedMaxMmsPduSize = negotiatedMaxPdu,
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
            var message = $"G2.1 DefineNamedVariableList qualification transport fault: {ex.GetType().Name}: {ex.Message}";
            await MarkProtocolFaultAsync().ConfigureAwait(false);
            LastDiscoveryAttemptSummary = message;
            return new MmsDynamicDataSetQualificationServiceEvidence
            {
                Service = "DefineNamedVariableList",
                Attempted = true,
                IsSuccess = false,
                InvokeId = invokeId,
                DataSetReference = dataSetReference,
                MemberReferences = memberReferences.ToArray(),
                RequestByteCount = request.Length,
                NegotiatedMaxMmsPduSize = negotiatedMaxPdu,
                RequestHex = requestHex,
                ResponseHex = responseHex,
                StateBefore = stateBefore,
                StateAfter = State,
                ReceiveRoutingSummary = route,
                Message = message
            };
        }
    }

    private async Task<MmsDynamicDataSetQualificationServiceEvidence> SendQualificationDeleteAsync(
        string dataSetReference,
        IReadOnlyList<string> memberReferences,
        int? negotiatedMaxPdu,
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
            return new MmsDynamicDataSetQualificationServiceEvidence
            {
                Service = "DeleteNamedVariableList",
                Attempted = true,
                IsSuccess = result.IsSuccess,
                InvokeId = invokeId,
                DataSetReference = dataSetReference,
                MemberReferences = memberReferences.ToArray(),
                RequestByteCount = request.Length,
                NegotiatedMaxMmsPduSize = negotiatedMaxPdu,
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
            var message = $"G2.1 DeleteNamedVariableList qualification transport fault: {ex.GetType().Name}: {ex.Message}";
            await MarkProtocolFaultAsync().ConfigureAwait(false);
            LastDiscoveryAttemptSummary = message;
            return new MmsDynamicDataSetQualificationServiceEvidence
            {
                Service = "DeleteNamedVariableList",
                Attempted = true,
                IsSuccess = false,
                InvokeId = invokeId,
                DataSetReference = dataSetReference,
                MemberReferences = memberReferences.ToArray(),
                RequestByteCount = request.Length,
                NegotiatedMaxMmsPduSize = negotiatedMaxPdu,
                RequestHex = requestHex,
                ResponseHex = responseHex,
                StateBefore = stateBefore,
                StateAfter = State,
                ReceiveRoutingSummary = route,
                Message = message
            };
        }
    }

    private static MmsDynamicDataSetQualificationProbeResult BuildQualificationResult(
        bool isSuccess,
        MmsDynamicDataSetQualificationFailureStage failureStage,
        string dataSetReference,
        IReadOnlyList<string> requestedMemberReferences,
        IReadOnlyList<string> returnedMemberReferences,
        int applicationSafetyMemberLimit,
        int defineRequestByteCount,
        int? negotiatedMaxPdu,
        bool requestWithinKnownNegotiatedPdu,
        MmsDynamicDataSetQualificationServiceEvidence define,
        bool directoryAttempted,
        bool directoryVerified,
        string directoryMessage,
        string directoryResponseHex,
        MmsDynamicDataSetQualificationServiceEvidence delete,
        IReadOnlyList<MmsReportAttributeWriteStep> steps,
        IReadOnlyList<string> evidence)
        => new()
        {
            IsSuccess = isSuccess,
            FailureStage = failureStage,
            DataSetReference = dataSetReference,
            RequestedMemberReferences = requestedMemberReferences.ToArray(),
            ReturnedMemberReferences = returnedMemberReferences.ToArray(),
            ApplicationSafetyMemberLimit = applicationSafetyMemberLimit,
            DefineRequestByteCount = defineRequestByteCount,
            NegotiatedMaxMmsPduSize = negotiatedMaxPdu,
            RequestWithinKnownNegotiatedPdu = requestWithinKnownNegotiatedPdu,
            DefineEvidence = define,
            DirectoryAttempted = directoryAttempted,
            DirectoryVerified = directoryVerified,
            DirectoryMessage = directoryMessage,
            DirectoryResponseHex = directoryResponseHex,
            DeleteEvidence = delete,
            WriteSteps = steps.ToArray(),
            EvidenceLines = evidence.ToArray()
        };

    private static string ToQualificationMemberReference(MmsObjectReference member)
    {
        if (string.IsNullOrWhiteSpace(member.Domain) || string.IsNullOrWhiteSpace(member.Item))
            throw new ArgumentException("Dynamic DataSet qualification requires fully resolved MMS member domain/item references.", nameof(member));

        return $"{member.Domain}/{member.Item}";
    }

    private static void AppendQualificationServiceEvidence(
        ICollection<string> lines,
        string label,
        MmsDynamicDataSetQualificationServiceEvidence serviceEvidence)
    {
        lines.Add($"G2.1 NVL QUALIFY {label}: {serviceEvidence.Summary}");
        lines.Add($"G2.1 NVL QUALIFY {label} members={FormatMembers(serviceEvidence.MemberReferences)}");
        lines.Add($"G2.1 NVL QUALIFY {label} requestHEX={QualificationTextOrNone(serviceEvidence.RequestHex)}");
        lines.Add($"G2.1 NVL QUALIFY {label} responseHEX={QualificationTextOrNone(serviceEvidence.ResponseHex)}");
    }

    private static string FormatMembers(IReadOnlyList<string> members)
        => members.Count == 0 ? "<none>" : string.Join(" | ", members);

    private static string QualificationTextOrNone(string? value)
        => string.IsNullOrWhiteSpace(value) ? "<none>" : value.Trim();
}
