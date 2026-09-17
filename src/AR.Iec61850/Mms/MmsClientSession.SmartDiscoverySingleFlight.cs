namespace AR.Iec61850.Mms;

/// <summary>
/// Association-scoped single-flight wrapper for smart discovery. Multiple consumers
/// asking for the same bounded discovery profile on one live MMS association share the
/// same wire operation. Cancelling one waiter never cancels the shared discovery.
/// </summary>
public sealed partial class MmsClientSession
{
    private readonly object _smartDiscoverySingleFlightSync = new();
    private object? _smartDiscoverySingleFlightAssociationMarker;
    private string _smartDiscoverySingleFlightHost = string.Empty;
    private int _smartDiscoverySingleFlightPort;
    private SmartDiscoverySingleFlightKey? _smartDiscoverySingleFlightKey;
    private Task<MmsDiscoveryResult>? _smartDiscoverySingleFlightTask;

    /// <summary>
    /// Runs <see cref="DiscoverSmartAsync"/> at most once for an identical smart
    /// discovery profile on the current accepted association. The underlying operation
    /// is intentionally independent from an individual caller's cancellation token;
    /// waiter cancellation only stops that waiter. Association loss still terminates
    /// the shared operation through the normal transport/receive-pump fault path.
    /// </summary>
    public async Task<MmsDiscoveryResult> DiscoverSmartSingleFlightAsync(
        MmsSmartDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();
        options ??= new MmsSmartDiscoveryOptions();

        var associationMarker = LastAssociationAttempts;
        var host = _lastHost;
        var port = _lastPort;
        var key = SmartDiscoverySingleFlightKey.From(options);

        Task<MmsDiscoveryResult> sharedTask;
        lock (_smartDiscoverySingleFlightSync)
        {
            if (!IsSameSmartDiscoveryAssociation(associationMarker, host, port))
                ResetSmartDiscoverySingleFlightUnsafe();

            if (_smartDiscoverySingleFlightTask != null &&
                _smartDiscoverySingleFlightKey.HasValue &&
                _smartDiscoverySingleFlightKey.Value.Equals(key))
            {
                sharedTask = _smartDiscoverySingleFlightTask;
            }
            else
            {
                _smartDiscoverySingleFlightAssociationMarker = associationMarker;
                _smartDiscoverySingleFlightHost = host;
                _smartDiscoverySingleFlightPort = port;
                _smartDiscoverySingleFlightKey = key;

                sharedTask = DiscoverSmartAsync(options, CancellationToken.None);
                _smartDiscoverySingleFlightTask = sharedTask;
                ObserveSmartDiscoverySingleFlightCompletion(sharedTask);
            }
        }

        return await sharedTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the domain-variable inventory already established by the authoritative
    /// smart discovery on this association. Consumers such as Control must reuse this
    /// evidence instead of issuing another VMD + per-domain GetNameList sweep.
    ///
    /// If no complete smart inventory exists for the current association, the legacy
    /// browse remains the compatibility fallback. This keeps standalone control usage
    /// safe while making a smart-discovered session wire-idempotent for later consumers.
    /// </summary>
    internal async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAuthoritativeDomainVariableNamesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureMmsReady();

        Task<MmsDiscoveryResult>? sharedTask = null;
        lock (_smartDiscoverySingleFlightSync)
        {
            if (IsSameSmartDiscoveryAssociation(LastAssociationAttempts, _lastHost, _lastPort))
                sharedTask = _smartDiscoverySingleFlightTask;
        }

        if (sharedTask != null)
        {
            try
            {
                var discovery = await sharedTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (IsReusableSmartDiscoveryResult(discovery))
                    return discovery.Snapshot.DomainVariables;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The shared discovery owns its own lifetime. If it ended unexpectedly,
                // fall through to the compatibility browse while the association lives.
            }
            catch (Exception) when (IsMmsInitiated)
            {
                // Do not let a faulted/partial shared generation poison later consumers.
                // A live association may still support the conservative legacy browse.
            }
        }

        return await DiscoverDomainVariableNamesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void ObserveSmartDiscoverySingleFlightCompletion(Task<MmsDiscoveryResult> task)
    {
        _ = task.ContinueWith(
            completed =>
            {
                var reusable = completed.Status == TaskStatus.RanToCompletion &&
                               IsReusableSmartDiscoveryResult(completed.Result);

                if (reusable)
                    return;

                lock (_smartDiscoverySingleFlightSync)
                {
                    if (ReferenceEquals(_smartDiscoverySingleFlightTask, completed))
                        ResetSmartDiscoverySingleFlightUnsafe();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private bool IsSameSmartDiscoveryAssociation(object associationMarker, string host, int port)
        => IsMmsInitiated &&
           ReferenceEquals(_smartDiscoverySingleFlightAssociationMarker, associationMarker) &&
           string.Equals(_smartDiscoverySingleFlightHost, host, StringComparison.OrdinalIgnoreCase) &&
           _smartDiscoverySingleFlightPort == port;

    private bool IsReusableSmartDiscoveryResult(MmsDiscoveryResult result)
    {
        if (!IsMmsInitiated || result.Snapshot.DomainCount <= 0)
            return false;

        // DiscoverSmartAsync deliberately returns partial evidence for recoverable MMS
        // failures. Partial evidence is useful to the initiating caller but must never
        // poison the association cache; a later caller must be allowed to retry.
        var summary = result.Summary ?? string.Empty;
        return summary.Contains("domain-list=complete", StringComparison.OrdinalIgnoreCase) &&
               summary.Contains("incompleteChains=0", StringComparison.OrdinalIgnoreCase);
    }

    private void ResetSmartDiscoverySingleFlightUnsafe()
    {
        _smartDiscoverySingleFlightAssociationMarker = null;
        _smartDiscoverySingleFlightHost = string.Empty;
        _smartDiscoverySingleFlightPort = 0;
        _smartDiscoverySingleFlightKey = null;
        _smartDiscoverySingleFlightTask = null;
    }

    private readonly record struct SmartDiscoverySingleFlightKey(
        int MaxConcurrentChains,
        int UnknownPeerMaxConcurrentChains,
        int MaxDomains,
        int MaxVariableNamesPerDomain,
        int MaxVariableListNamesPerDomain,
        int MaxNameListPages,
        bool ProbeReportAttributes,
        int MaxReportAttributeProbes,
        bool ReadDataSetDirectories,
        int MaxDataSetDirectoryReads,
        string PriorityDomains)
    {
        public static SmartDiscoverySingleFlightKey From(MmsSmartDiscoveryOptions options)
        {
            var priorityDomains = string.Join(
                "\u001F",
                (options.PriorityDomains ?? Array.Empty<string>())
                    .Where(domain => !string.IsNullOrWhiteSpace(domain))
                    .Select(domain => domain.Trim().ToUpperInvariant()));

            return new SmartDiscoverySingleFlightKey(
                options.MaxConcurrentChains,
                options.UnknownPeerMaxConcurrentChains,
                options.MaxDomains,
                options.MaxVariableNamesPerDomain,
                options.MaxVariableListNamesPerDomain,
                options.MaxNameListPages,
                options.ProbeReportAttributes,
                options.MaxReportAttributeProbes,
                options.ReadDataSetDirectories,
                options.MaxDataSetDirectoryReads,
                priorityDomains);
        }
    }
}
