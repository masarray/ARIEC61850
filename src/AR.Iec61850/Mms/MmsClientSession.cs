using AR.Iec61850.Acse;
using AR.Iec61850.Diagnostics;
using AR.Iec61850.Osi;

namespace AR.Iec61850.Mms;

public enum MmsAssociationState
{
    Disconnected,
    TcpConnected,
    CotpConnected,
    AcsePending,
    MmsInitiated,
    MmsInitiateFailed
}

public sealed class MmsClientSession : IAsyncDisposable
{
    private readonly TpktClient _tpkt = new();
    private readonly CotpClient _cotp;
    private string _lastHost = string.Empty;
    private int _lastPort = 102;
    private TimeSpan _lastTimeout = TimeSpan.FromSeconds(5);
    private int _nextInvokeId;

    public MmsClientSession()
    {
        _cotp = new CotpClient(_tpkt);
    }

    public MmsAssociationState State { get; private set; } = MmsAssociationState.Disconnected;
    public bool IsTcpConnected => _tpkt.IsConnected;
    public bool IsTransportConnected => _tpkt.IsConnected && _cotp.IsConnected;
    public bool IsMmsInitiated => State == MmsAssociationState.MmsInitiated;
    public string LastHandshakeMessage { get; private set; } = string.Empty;
    public string LastAssociationResponseHex { get; private set; } = string.Empty;
    public IReadOnlyList<AcseAssociationAttempt> LastAssociationAttempts { get; private set; } = Array.Empty<AcseAssociationAttempt>();
    public string LastAssociationAttemptSummary => LastAssociationAttempts.Count == 0
        ? string.Empty
        : string.Join(" | ", LastAssociationAttempts.Select(a => a.Summary));
    public string LastDiscoveryRequestHex { get; private set; } = string.Empty;
    public string LastDiscoveryResponseHex { get; private set; } = string.Empty;
    public string LastDiscoveryAttemptSummary { get; private set; } = string.Empty;
    public string LastReadRequestHex { get; private set; } = string.Empty;
    public string LastReadResponseHex { get; private set; } = string.Empty;
    public IReadOnlyList<MmsReadAttempt> LastReadAttempts { get; private set; } = Array.Empty<MmsReadAttempt>();
    public string LastReadAttemptSummary => LastReadAttempts.Count == 0
        ? string.Empty
        : string.Join(" | ", LastReadAttempts.Select(a => a.Summary));

    public Task ConnectAsync(string host, int port = 102, CancellationToken cancellationToken = default)
        => ConnectAsync(host, port, TimeSpan.FromSeconds(5), cancellationToken);

    public async Task ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        _lastHost = host;
        _lastPort = port <= 0 ? 102 : port;
        _lastTimeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : timeout;
        _nextInvokeId = 0;
        LastReadRequestHex = string.Empty;
        LastReadResponseHex = string.Empty;
        LastReadAttempts = Array.Empty<MmsReadAttempt>();
        LastDiscoveryRequestHex = string.Empty;
        LastDiscoveryResponseHex = string.Empty;
        LastDiscoveryAttemptSummary = string.Empty;

        await AssociateAsync(resetAssociationDiagnostics: true, cancellationToken).ConfigureAwait(false);

        if (!IsMmsInitiated)
            throw new InvalidDataException(string.IsNullOrWhiteSpace(LastHandshakeMessage) ? "ACSE/MMS association failed." : LastHandshakeMessage);
    }

    public async Task<MmsDiscoveryResult> DiscoverAsync(
        bool probeReportAttributes = true,
        int maxReportAttributeProbes = 32,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();

        var domainVariables = await DiscoverDomainVariableNamesAsync(cancellationToken).ConfigureAwait(false);
        var domainVariableLists = await DiscoverDomainVariableListNamesAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = new MmsDiscoverySnapshot
        {
            DomainVariables = domainVariables,
            DomainVariableLists = domainVariableLists
        };

        var inventory = MmsReportDiscoveryMapper.BuildInventory(snapshot);
        var iedDirectory = MmsIedModelDirectoryBuilder.Build(snapshot);
        if (probeReportAttributes)
            await EnrichReportInventoryAsync(inventory, Math.Max(0, maxReportAttributeProbes), cancellationToken).ConfigureAwait(false);

        return new MmsDiscoveryResult
        {
            Snapshot = snapshot,
            ReportInventory = inventory,
            IedDirectory = iedDirectory,
            Summary = $"Native MMS GetNameList discovery: LD={snapshot.DomainCount}, raw variables={snapshot.RawVariableCount}, FC-points={iedDirectory.PointCount}, datasets={inventory.DataSets.Count}, RCB={inventory.ReportControls.Count} (BRCB={inventory.BufferedCount}, URCB={inventory.UnbufferedCount}). {LastDiscoveryAttemptSummary}"
        };
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverDomainVariableNamesAsync(CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();

        var domainsResult = await GetNameListPagedAsync(MmsGetNameListObjectClass.Domain, null, cancellationToken).ConfigureAwait(false);
        if (!domainsResult.IsSuccess)
        {
            LastDiscoveryAttemptSummary = $"Domain GetNameList failed: {domainsResult.Message}";
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var summary = new List<string>();
        var domains = domainsResult.Names
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(256)
            .ToArray();

        summary.Add($"LD/domain={domains.Length}");

        foreach (var domain in domains)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var variables = await GetNameListPagedAsync(MmsGetNameListObjectClass.NamedVariable, domain, cancellationToken).ConfigureAwait(false);
            if (variables.IsSuccess)
            {
                var names = variables.Names
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Take(20000)
                    .ToArray();

                result[domain] = names;
                summary.Add($"{domain}:var={names.Length}");
            }
            else
            {
                result[domain] = Array.Empty<string>();
                summary.Add($"{domain}:var=failed:{variables.Message}");
            }
        }

        LastDiscoveryAttemptSummary = "Native GetNameList discovery: " + string.Join(" | ", summary.Take(20));
        return result;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> DiscoverDomainVariableListNamesAsync(CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();

        var domainsResult = await GetNameListPagedAsync(MmsGetNameListObjectClass.Domain, null, cancellationToken).ConfigureAwait(false);
        if (!domainsResult.IsSuccess)
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var domain in domainsResult.Names.Take(256))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lists = await GetNameListPagedAsync(MmsGetNameListObjectClass.NamedVariableList, domain, cancellationToken).ConfigureAwait(false);
            result[domain] = lists.IsSuccess
                ? lists.Names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();
        }

        return result;
    }

    public async Task<MmsNameListResult> GetNameListPagedAsync(
        MmsGetNameListObjectClass objectClass,
        string? domainId,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();

        var names = new List<string>();
        var continueAfter = string.Empty;
        var page = 0;
        MmsNameListResult? last = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            page++;

            var invokeId = NextInvokeId();
            var request = MmsGetNameListRequest.Build(invokeId, objectClass, domainId, string.IsNullOrWhiteSpace(continueAfter) ? null : continueAfter);
            LastDiscoveryRequestHex = HexDump.ToCompactString(request);

            try
            {
                var response = await SendPresentationPayloadAsync(request, cancellationToken).ConfigureAwait(false);
                last = MmsGetNameListResponseDecoder.Decode(response, invokeId);
                LastDiscoveryResponseHex = last.ResponseHexPreview;

                if (!last.IsSuccess)
                {
                    LastDiscoveryAttemptSummary = $"GetNameList {objectClass}/{domainId ?? "VMD"} page {page} failed: {last.Message}";
                    return new MmsNameListResult
                    {
                        IsSuccess = false,
                        Names = names,
                        MoreFollows = false,
                        Message = last.Message,
                        ResponseHexPreview = last.ResponseHexPreview
                    };
                }

                foreach (var name in last.Names)
                {
                    if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                        names.Add(name);
                }

                continueAfter = last.Names.LastOrDefault() ?? continueAfter;
                LastDiscoveryAttemptSummary = $"GetNameList {objectClass}/{domainId ?? "VMD"}: page={page}, total={names.Count}, more={last.MoreFollows}.";
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
            {
                await MarkProtocolFaultAsync().ConfigureAwait(false);
                LastDiscoveryAttemptSummary = $"GetNameList {objectClass}/{domainId ?? "VMD"} transport fault on page {page}: {ex.GetType().Name}: {ex.Message}";
                return new MmsNameListResult
                {
                    IsSuccess = false,
                    Names = names,
                    MoreFollows = false,
                    Message = LastDiscoveryAttemptSummary,
                    ResponseHexPreview = LastDiscoveryResponseHex
                };
            }
        }
        while (last.MoreFollows && page < 64 && !string.IsNullOrWhiteSpace(continueAfter));

        return new MmsNameListResult
        {
            IsSuccess = true,
            Names = names,
            MoreFollows = last?.MoreFollows ?? false,
            Message = $"GetNameList {objectClass}/{domainId ?? "VMD"} completed: {names.Count} name(s), pages={page}.",
            ResponseHexPreview = last?.ResponseHexPreview ?? string.Empty
        };
    }

    public async Task<MmsReadResult> ReadSingleVariableAsync(
        MmsObjectReference reference,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();

        var attempts = new List<MmsReadAttempt>();
        var candidates = BuildReadCandidates(reference);
        var payloadProfiles = BuildPayloadProfiles();

        foreach (var (objectProfile, candidate) in candidates)
        {
            foreach (var payloadProfile in payloadProfiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var invokeId = NextInvokeId();
                var request = MmsReadRequest.BuildSingleVariableRead(invokeId, candidate, payloadProfile);
                var requestHex = HexDump.ToCompactString(request);
                LastReadRequestHex = requestHex;

                MmsReadResult result;
                try
                {
                    var response = await SendPresentationPayloadAsync(request, cancellationToken).ConfigureAwait(false);
                    result = MmsReadResponseDecoder.DecodeSingleVariable(response, invokeId);
                    LastReadResponseHex = result.ResponseHexPreview;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
                {
                    result = new MmsReadResult
                    {
                        IsSuccess = false,
                        Message = $"Native MMS read transport fault after {payloadProfile}: {ex.GetType().Name}: {ex.Message}",
                        ResponseHexPreview = LastReadResponseHex
                    };
                    await MarkProtocolFaultAsync().ConfigureAwait(false);
                }

                attempts.Add(new MmsReadAttempt
                {
                    ObjectProfile = objectProfile,
                    PayloadProfile = payloadProfile,
                    Reference = candidate,
                    RequestHexPreview = requestHex,
                    Result = result
                });
                LastReadAttempts = attempts.ToArray();

                if (result.IsSuccess)
                {
                    LastHandshakeMessage = $"Native MMS Confirmed-Read succeeded using {objectProfile}/{payloadProfile}: {candidate}. {result.Message}";
                    return result;
                }

                if (!ShouldTryNextPayloadProfile(result))
                    break;
            }
        }

        var last = attempts.LastOrDefault()?.Result ?? new MmsReadResult
        {
            IsSuccess = false,
            Message = "Native MMS Confirmed-Read did not return a decodable value.",
            ResponseHexPreview = LastReadResponseHex
        };
        LastHandshakeMessage = LastReadAttemptSummary;
        return last;
    }


    public async Task<MmsSmartReadResult> ReadSmartAsync(
        MmsIedModelDirectory directory,
        string reference,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(directory);

        var resolve = MmsFcResolver.Resolve(directory, reference);
        var selected = resolve.BestCandidate;
        if (selected == null)
        {
            return new MmsSmartReadResult
            {
                ResolveResult = resolve,
                ReadResult = new MmsReadResult
                {
                    IsSuccess = false,
                    Message = resolve.Message
                }
            };
        }

        var read = await ReadSingleVariableAsync(selected.ToObjectReference(), cancellationToken).ConfigureAwait(false);
        return new MmsSmartReadResult
        {
            ResolveResult = resolve,
            SelectedPoint = selected,
            ReadResult = read
        };
    }

    public async Task<MmsDataSetDirectoryResult> GetDataSetDirectoryAsync(
        string dataSetReference,
        MmsIedModelDirectory? directory = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();

        var invokeId = NextInvokeId();
        var request = MmsDataSetDirectoryRequest.Build(invokeId, dataSetReference);
        LastDiscoveryRequestHex = HexDump.ToCompactString(request);

        try
        {
            var response = await SendPresentationPayloadAsync(request, cancellationToken).ConfigureAwait(false);
            var result = MmsDataSetDirectoryResponseDecoder.Decode(response, invokeId, dataSetReference, directory);
            LastDiscoveryResponseHex = result.ResponseHexPreview;
            LastDiscoveryAttemptSummary = result.Summary;
            return result;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException)
        {
            await MarkProtocolFaultAsync().ConfigureAwait(false);
            var result = new MmsDataSetDirectoryResult
            {
                IsSuccess = false,
                DataSetReference = dataSetReference,
                Message = $"DataSet directory transport fault: {ex.GetType().Name}: {ex.Message}",
                ResponseHexPreview = LastDiscoveryResponseHex
            };
            LastDiscoveryAttemptSummary = result.Summary;
            return result;
        }
    }

    public async Task<IReadOnlyList<MmsDataSetDirectoryResult>> GetDataSetDirectoriesAsync(
        IEnumerable<string> dataSetReferences,
        MmsIedModelDirectory? directory = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        ArgumentNullException.ThrowIfNull(dataSetReferences);

        var results = new List<MmsDataSetDirectoryResult>();
        foreach (var dataSetReference in dataSetReferences.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await GetDataSetDirectoryAsync(dataSetReference, directory, cancellationToken).ConfigureAwait(false);
            results.Add(result);

            if (!IsMmsInitiated)
                break;
        }

        return results;
    }

    public async Task<byte[]> SendPresentationPayloadAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        if (!IsTransportConnected)
            throw new InvalidOperationException("Native IEC 61850 transport is not connected.");

        await _cotp.SendDataAsync(payload, cancellationToken).ConfigureAwait(false);
        return await _cotp.ReceiveDataAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        State = MmsAssociationState.Disconnected;
        await ResetTransportAsync().ConfigureAwait(false);
    }

    private async Task AssociateAsync(bool resetAssociationDiagnostics, CancellationToken cancellationToken)
    {
        State = MmsAssociationState.Disconnected;
        if (resetAssociationDiagnostics)
        {
            LastHandshakeMessage = string.Empty;
            LastAssociationResponseHex = string.Empty;
            LastAssociationAttempts = Array.Empty<AcseAssociationAttempt>();
        }

        var attempts = new List<AcseAssociationAttempt>();
        Exception? lastException = null;

        foreach (var profile in AcseMmsInitiateRequest.BuildAssociationProfiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ResetTransportAsync().ConfigureAwait(false);

            try
            {
                await _tpkt.ConnectAsync(_lastHost, _lastPort, _lastTimeout, cancellationToken).ConfigureAwait(false);
                State = MmsAssociationState.TcpConnected;

                await _cotp.ConnectAsync(cancellationToken).ConfigureAwait(false);
                State = MmsAssociationState.CotpConnected;
                LastHandshakeMessage = $"{profile.Name}: {_cotp.LastConnectionConfirm?.Message ?? "COTP connection confirmed."}";

                var result = await TryInitiateMmsAssociationAsync(profile, cancellationToken).ConfigureAwait(false);
                attempts.Add(new AcseAssociationAttempt
                {
                    ProfileName = profile.Name,
                    IsAccepted = result.IsAccepted,
                    Message = result.Message,
                    ResponseHexPreview = result.ResponseHexPreview
                });
                LastAssociationAttempts = attempts.ToArray();

                if (result.IsAccepted)
                {
                    State = MmsAssociationState.MmsInitiated;
                    LastHandshakeMessage = result.Message;
                    return;
                }

                State = MmsAssociationState.MmsInitiateFailed;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastException = ex;
                State = MmsAssociationState.MmsInitiateFailed;
                attempts.Add(new AcseAssociationAttempt
                {
                    ProfileName = profile.Name,
                    IsAccepted = false,
                    Message = $"{profile.Name}: transport/association exception: {ex.GetType().Name}: {ex.Message}",
                    ResponseHexPreview = LastAssociationResponseHex
                });
                LastAssociationAttempts = attempts.ToArray();
            }
        }

        await ResetTransportAsync().ConfigureAwait(false);
        State = MmsAssociationState.MmsInitiateFailed;
        LastHandshakeMessage = LastAssociationAttemptSummary;
        if (string.IsNullOrWhiteSpace(LastHandshakeMessage) && lastException != null)
            LastHandshakeMessage = $"Native ACSE/MMS association failed: {lastException.GetType().Name}: {lastException.Message}";
    }

    private async Task<AcseMmsInitiateResult> TryInitiateMmsAssociationAsync(AcseAssociationProfile profile, CancellationToken cancellationToken)
    {
        if (!IsTransportConnected)
            throw new InvalidOperationException("Native IEC 61850 transport is not connected.");

        State = MmsAssociationState.AcsePending;
        await _cotp.SendDataAsync(profile.Payload, cancellationToken).ConfigureAwait(false);
        var response = await _cotp.ReceiveDataAsync(cancellationToken).ConfigureAwait(false);
        var result = AcseMmsInitiateResult.Parse(response, profile.Name);
        LastAssociationResponseHex = result.ResponseHexPreview;
        LastHandshakeMessage = result.Message;
        return result;
    }

    private async Task EnrichReportInventoryAsync(
        MmsReportInventory inventory,
        int maxReportAttributeProbes,
        CancellationToken cancellationToken)
    {
        if (maxReportAttributeProbes <= 0 || inventory.ReportControls.Count == 0 || !IsMmsInitiated)
            return;

        foreach (var reportControl in inventory.ReportControls
                     .OrderByDescending(x => x.Buffered)
                     .ThenByDescending(x => x.LogicalNode.Equals("LLN0", StringComparison.OrdinalIgnoreCase))
                     .Take(maxReportAttributeProbes))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await TryReadReportAttributeAsync(reportControl, "DatSet", value =>
            {
                var text = NormalizeReportAttributeText(value);
                if (!string.IsNullOrWhiteSpace(text))
                    reportControl.DataSetReference = NormalizeReportedDataSetReference(reportControl.Domain, text);
            }, cancellationToken).ConfigureAwait(false);

            await TryReadReportAttributeIfPresentAsync(reportControl, "RptID", value => reportControl.ReportId = NormalizeReportAttributeText(value), cancellationToken).ConfigureAwait(false);
            await TryReadReportAttributeIfPresentAsync(reportControl, "ConfRev", value => reportControl.ConfRev = NormalizeReportAttributeText(value), cancellationToken).ConfigureAwait(false);
            await TryReadReportAttributeIfPresentAsync(reportControl, "IntgPd", value => reportControl.IntegrityPeriodMs = NormalizeReportAttributeText(value), cancellationToken).ConfigureAwait(false);
            await TryReadReportAttributeIfPresentAsync(reportControl, "RptEna", value => reportControl.EnabledState = NormalizeReportAttributeText(value), cancellationToken).ConfigureAwait(false);
            await TryReadReportAttributeIfPresentAsync(reportControl, "BufTm", value => reportControl.BufferTimeMs = NormalizeReportAttributeText(value), cancellationToken).ConfigureAwait(false);
            await TryReadReportAttributeIfPresentAsync(reportControl, "TrgOps", value => reportControl.TriggerOptions = NormalizeReportAttributeText(value), cancellationToken).ConfigureAwait(false);
            await TryReadReportAttributeIfPresentAsync(reportControl, "OptFlds", value => reportControl.OptionalFields = NormalizeReportAttributeText(value), cancellationToken).ConfigureAwait(false);

            if (reportControl.Buffered)
                await TryReadReportAttributeIfPresentAsync(reportControl, "ResvTms", value => reportControl.ReservationTimeSeconds = NormalizeReportAttributeText(value), cancellationToken).ConfigureAwait(false);
            else
                await TryReadReportAttributeIfPresentAsync(reportControl, "Resv", value => reportControl.ReservationState = NormalizeReportAttributeText(value), cancellationToken).ConfigureAwait(false);

            reportControl.Status = HasUsefulReportProbeData(reportControl) ? "Attribute-probed" : reportControl.Status;
        }
    }

    private async Task TryReadReportAttributeIfPresentAsync(
        MmsReportControlCandidate reportControl,
        string attribute,
        Action<MmsDataValue?> apply,
        CancellationToken cancellationToken)
    {
        if (!reportControl.Attributes.Contains(attribute, StringComparer.OrdinalIgnoreCase))
            return;

        await TryReadReportAttributeAsync(reportControl, attribute, apply, cancellationToken).ConfigureAwait(false);
    }

    private async Task TryReadReportAttributeAsync(
        MmsReportControlCandidate reportControl,
        string attribute,
        Action<MmsDataValue?> apply,
        CancellationToken cancellationToken)
    {
        try
        {
            var reference = MmsObjectReference.Parse($"{reportControl.Reference}.{attribute}", reportControl.FunctionalConstraint);
            var result = await ReadSingleVariableAsync(reference, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
                apply(result.Value);
            else if (reportControl.Status == "Discovered")
                reportControl.Status = $"Attribute probe partial: {attribute}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (reportControl.Status == "Discovered")
                reportControl.Status = $"Attribute probe partial: {attribute} {ex.GetType().Name}";
        }
    }


    private static bool HasUsefulReportProbeData(MmsReportControlCandidate reportControl)
        => !string.IsNullOrWhiteSpace(reportControl.DataSetReference) ||
           !string.IsNullOrWhiteSpace(reportControl.ReportId) ||
           !string.IsNullOrWhiteSpace(reportControl.ConfRev) ||
           !string.IsNullOrWhiteSpace(reportControl.EnabledState) ||
           !string.IsNullOrWhiteSpace(reportControl.ReservationState) ||
           !string.IsNullOrWhiteSpace(reportControl.ReservationTimeSeconds);

    private void EnsureMmsReady()
    {
        if (!IsMmsInitiated)
            throw new InvalidOperationException($"Native IEC 61850 MMS association is not initiated. Current state: {State}.");
    }

    private int NextInvokeId()
    {
        var invokeId = Interlocked.Increment(ref _nextInvokeId);
        if (invokeId <= 0x7FFF)
            return invokeId;

        Interlocked.Exchange(ref _nextInvokeId, 1);
        return 1;
    }

    private async Task MarkProtocolFaultAsync()
    {
        State = MmsAssociationState.MmsInitiateFailed;
        await ResetTransportAsync().ConfigureAwait(false);
    }

    private async ValueTask ResetTransportAsync()
    {
        _cotp.Reset();
        await _tpkt.DisposeAsync().ConfigureAwait(false);
    }

    private static IReadOnlyList<(string Profile, MmsObjectReference Reference)> BuildReadCandidates(MmsObjectReference reference)
    {
        var candidates = new List<(string Profile, MmsObjectReference Reference)>
        {
            ("PrimaryFcNamedVariable", reference)
        };

        var noFunctionalConstraint = reference.WithoutFunctionalConstraint();
        if (!string.Equals(noFunctionalConstraint.Item, reference.Item, StringComparison.OrdinalIgnoreCase))
            candidates.Add(("AlternateNoFcNamedVariable", noFunctionalConstraint));

        return candidates;
    }

    private static IReadOnlyList<MmsReadPayloadProfile> BuildPayloadProfiles()
        =>
        [
            MmsReadPayloadProfile.PresentationDataValues,
            MmsReadPayloadProfile.PresentationDataValuesWithSpecificationResult,
            MmsReadPayloadProfile.SessionDataOnly,
            MmsReadPayloadProfile.RawMmsPdu
        ];

    private static bool ShouldTryNextPayloadProfile(MmsReadResult result)
    {
        if (result.IsSuccess)
            return false;

        var message = result.Message ?? string.Empty;

        if (message.Contains("AccessResult.failure", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("object", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("access", StringComparison.OrdinalIgnoreCase))
            return false;

        return message.Contains("transport fault", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Expected MMS Confirmed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Reject", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("Abort", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("decode failed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("no decodable MMS Data", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeReportAttributeText(MmsDataValue? value)
    {
        if (value == null)
            return string.Empty;

        return MmsDataCodec.ToDisplayString(value).Trim();
    }

    private static string NormalizeReportedDataSetReference(string domain, string value)
    {
        var text = value.Trim().Replace('$', '.');
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        if (text.Contains('/'))
            return text;

        return text.Contains('.')
            ? $"{domain}/{text}"
            : $"{domain}/LLN0.{text}";
    }
}
