using AR.Iec61850.Mms;

namespace AR.Iec61850.Engineering;

public sealed class Iec61850Client : IAsyncDisposable
{
    private readonly MmsClientSession _session;

    public Iec61850Client()
        : this(new MmsClientSession())
    {
    }

    internal Iec61850Client(MmsClientSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public bool IsMmsInitiated => _session.IsMmsInitiated;
    public string LastAssociationSummary => _session.LastAssociationAttemptSummary;
    public string LastDiscoverySummary => _session.LastDiscoveryAttemptSummary;

    public async Task<Iec61850ServiceResult<Iec61850EngineeringProfile>> DiscoverEngineeringProfileAsync(
        Iec61850EngineeringProfileOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Host))
            return Iec61850ServiceResult<Iec61850EngineeringProfile>.Failure("HOST_REQUIRED", "MMS host/IP is required.", "Pass a reachable IED or simulator endpoint.");

        try
        {
            await _session.ConnectAsync(options.Host, options.Port, options.Timeout, cancellationToken).ConfigureAwait(false);
            var discovery = await _session.DiscoverAsync(options.ProbeReportAttributes, options.MaxReportAttributeProbes, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<MmsDataSetDirectoryResult> directories = Array.Empty<MmsDataSetDirectoryResult>();

            if (options.ReadDataSetDirectories && discovery.ReportInventory.DataSets.Count > 0)
            {
                var references = discovery.ReportInventory.DataSets
                    .Select(x => x.Reference)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(Math.Max(0, options.MaxDataSetDirectories))
                    .ToArray();

                directories = await _session.GetDataSetDirectoriesAsync(references, discovery.IedDirectory, cancellationToken).ConfigureAwait(false);
            }

            var profile = Iec61850EngineeringProfileBuilder.Build(discovery, directories, options);
            return Iec61850ServiceResult<Iec61850EngineeringProfile>.Success(profile, profile.Summary, profile.Diagnostics);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException or ArgumentException)
        {
            return Iec61850ServiceResult<Iec61850EngineeringProfile>.Failure(
                "DISCOVERY_PROFILE_FAILED",
                $"Engineering profile discovery failed: {ex.GetType().Name}: {ex.Message}",
                "Verify endpoint reachability, port 102 access, OSI association parameters, and the selected network path.");
        }
    }


    public async Task<Iec61850ServiceResult<Iec61850ReportReadinessProfile>> DiscoverStaticReportReadinessProfileAsync(
        Iec61850ReportReadinessProfileOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Host))
            return Iec61850ServiceResult<Iec61850ReportReadinessProfile>.Failure("HOST_REQUIRED", "MMS host/IP is required.", "Pass a reachable IED or simulator endpoint.");

        try
        {
            await _session.ConnectAsync(options.Host, options.Port, options.Timeout, cancellationToken).ConfigureAwait(false);
            var discovery = await _session.DiscoverAsync(options.ProbeReportAttributes, options.MaxReportAttributeProbes, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<MmsDataSetDirectoryResult> directories = Array.Empty<MmsDataSetDirectoryResult>();

            if (options.ReadDataSetDirectories && discovery.ReportInventory.DataSets.Count > 0)
            {
                var references = discovery.ReportInventory.DataSets
                    .Select(x => x.Reference)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(Math.Max(0, options.MaxDataSetDirectories))
                    .ToArray();

                directories = await _session.GetDataSetDirectoriesAsync(references, discovery.IedDirectory, cancellationToken).ConfigureAwait(false);
            }

            var profile = Iec61850ReportReadinessProfileBuilder.BuildStatic(discovery, directories, options);
            return Iec61850ServiceResult<Iec61850ReportReadinessProfile>.Success(profile, profile.Summary, profile.Diagnostics);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException or InvalidOperationException or ArgumentException)
        {
            return Iec61850ServiceResult<Iec61850ReportReadinessProfile>.Failure(
                "REPORT_READINESS_PROFILE_FAILED",
                $"Report readiness profile discovery failed: {ex.GetType().Name}: {ex.Message}",
                "Verify endpoint reachability, port 102 access, OSI association parameters, RCB probes, and DataSet directory reads.");
        }
    }

    public ValueTask DisposeAsync()
        => _session.DisposeAsync();
}
