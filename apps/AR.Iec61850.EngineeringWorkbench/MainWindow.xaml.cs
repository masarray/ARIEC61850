using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using AR.Iec61850.Capture;
using AR.Iec61850.Diagnostics.Binding;
using AR.Iec61850.Diagnostics.Goose;
using AR.Iec61850.Diagnostics.SampledValues;
using AR.Iec61850.EngineeringWorkbench.Models;
using AR.Iec61850.EngineeringWorkbench.ViewModels;
using AR.Iec61850.Monitoring;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Simulation;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WinForms = System.Windows.Forms;

namespace AR.Iec61850.EngineeringWorkbench;

public partial class MainWindow : System.Windows.Window
{
    private readonly EngineeringWorkbenchViewModel _viewModel = new();
    private CancellationTokenSource? _cancellation;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.SclPath = ResolveDefaultSclPath();
        _viewModel.EvidenceFolder = ResolveDefaultEvidenceFolder();
    }

    private void BrowseScl_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "Open SCL file",
            Filter = "SCL files (*.scd;*.cid;*.icd;*.iid;*.ssd;*.sed)|*.scd;*.cid;*.icd;*.iid;*.ssd;*.sed|XML files (*.xml)|*.xml|All files (*.*)|*.*",
            FileName = string.IsNullOrWhiteSpace(_viewModel.SclPath) ? string.Empty : Path.GetFileName(_viewModel.SclPath),
            InitialDirectory = SafeDirectory(_viewModel.SclPath)
        };

        if (dialog.ShowDialog(this) == true)
            _viewModel.SclPath = dialog.FileName;
    }

    private void BrowsePcap_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "Open PCAP file",
            Filter = "PCAP files (*.pcap;*.cap)|*.pcap;*.cap|All files (*.*)|*.*",
            FileName = string.IsNullOrWhiteSpace(_viewModel.PcapPath) ? string.Empty : Path.GetFileName(_viewModel.PcapPath),
            InitialDirectory = SafeDirectory(_viewModel.PcapPath)
        };

        if (dialog.ShowDialog(this) == true)
            _viewModel.PcapPath = dialog.FileName;
    }

    private void BrowseEvidenceFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Select evidence output folder",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_viewModel.EvidenceFolder) ? _viewModel.EvidenceFolder : ResolveDefaultEvidenceFolder()
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            _viewModel.EvidenceFolder = dialog.SelectedPath;
    }

    private async void RunWorkbench_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
            return;

        if (!File.Exists(_viewModel.SclPath))
        {
            System.Windows.MessageBox.Show(this, "Select a valid SCL file first.", "SCL required", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        _cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        _viewModel.IsBusy = true;
        _viewModel.ClearResults();
        _viewModel.Status = "Building SCL engineering profile...";

        try
        {
            var sclProfile = new SclEngineeringProfileBuilder().Load(_viewModel.SclPath);
            _viewModel.LastSclProfile = sclProfile;
            PopulateScl(sclProfile);

            var observed = Array.Empty<ProcessBusStreamSummary>();
            var observedPacketCount = 0;
            var observedDecodedCount = 0;
            if (File.Exists(_viewModel.PcapPath))
            {
                _viewModel.Status = "Reading PCAP and observing process-bus streams...";
                var observation = await Task.Run(() => ObservePcap(_viewModel.SclPath, _viewModel.PcapPath, _viewModel.NominalFrequencyHz), _cancellation.Token);
                observed = observation.Summaries;
                observedPacketCount = observation.PacketCount;
                observedDecodedCount = observation.DecodedFrameCount;
            }

            _viewModel.Status = "Building expected-vs-observed binding and GOOSE/SV diagnostics...";
            var sourceName = Path.GetFileName(_viewModel.SclPath);
            var binding = new ExpectedObservedBindingProfileBuilder().Build(sclProfile, observed, sourceName);
            var goose = new GooseDiagnosticsProfileBuilder().Build(sclProfile, observed, sourceName);
            var sv = new SampledValuesDiagnosticsProfileBuilder().Build(sclProfile, observed, sourceName);
            _viewModel.LastBindingProfile = binding;
            _viewModel.LastGooseProfile = goose;
            _viewModel.LastSampledValuesProfile = sv;
            PopulateBinding(binding);
            PopulateGoose(goose);
            PopulateSampledValues(sv);

            _viewModel.Status = "Running read-only MMS loopback alpha gate...";
            var mms = await new MmsReadOnlyServerLoopbackProfileBuilder().RunAsync(
                new MmsReadOnlyServerLoopbackOptions { Port = 0, ProbeTimeoutMilliseconds = 5000, SimulationSteps = 6 },
                _cancellation.Token);
            _viewModel.LastMmsLoopbackProfile = mms;
            PopulateMms(mms);

            PopulateMetrics(sclProfile, binding, goose, sv, mms, observedPacketCount, observedDecodedCount);
            PopulateFindings(sclProfile, binding, goose, sv, mms, null);
            _viewModel.Summary = BuildSummary(sclProfile, binding, goose, sv, mms, observedPacketCount, observedDecodedCount);
            _viewModel.Status = "Workbench run complete. Review findings before exporting evidence.";
        }
        catch (OperationCanceledException)
        {
            _viewModel.Status = "Workbench run cancelled.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or FormatException or JsonException or ArgumentException)
        {
            _viewModel.Status = $"Workbench run failed: {ex.GetType().Name}: {ex.Message}";
            _viewModel.Findings.Add(new FindingRow("High", "workbench", "WORKBENCH_RUN_FAILED", ex.Message));
        }
        finally
        {
            _viewModel.IsBusy = false;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private async void RunPublicAlpha_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
            return;

        var sclPath = File.Exists(_viewModel.SclPath) ? _viewModel.SclPath : ResolveDefaultSclPath();
        if (!File.Exists(sclPath))
        {
            System.Windows.MessageBox.Show(this, "No valid SCL file is available for the public alpha readiness gate.", "SCL required", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        _cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        _viewModel.IsBusy = true;
        _viewModel.ClearResults();
        _viewModel.Status = "Running public alpha readiness gate...";

        try
        {
            var readiness = await new PublicAlphaReadinessProfileBuilder().RunAsync(
                new PublicAlphaReadinessOptions { SclPath = sclPath, Port = 0, ProbeTimeoutMilliseconds = 5000, SimulationSteps = 6 },
                _cancellation.Token);

            _viewModel.LastPublicAlphaReadinessProfile = readiness;
            _viewModel.LastSclProfile = readiness.SclEngineering;
            _viewModel.LastBindingProfile = readiness.ProcessBusBinding;
            _viewModel.LastGooseProfile = readiness.GooseDiagnostics;
            _viewModel.LastSampledValuesProfile = readiness.SampledValuesDiagnostics;
            _viewModel.LastMmsLoopbackProfile = readiness.ReadOnlyMmsLoopback;

            PopulateScl(readiness.SclEngineering);
            PopulateBinding(readiness.ProcessBusBinding);
            PopulateGoose(readiness.GooseDiagnostics);
            PopulateSampledValues(readiness.SampledValuesDiagnostics);
            PopulateMms(readiness.ReadOnlyMmsLoopback);
            PopulateMetrics(readiness.SclEngineering, readiness.ProcessBusBinding, readiness.GooseDiagnostics, readiness.SampledValuesDiagnostics, readiness.ReadOnlyMmsLoopback, 0, 0);
            PopulateFindings(readiness.SclEngineering, readiness.ProcessBusBinding, readiness.GooseDiagnostics, readiness.SampledValuesDiagnostics, readiness.ReadOnlyMmsLoopback, readiness);

            _viewModel.Summary = readiness.Summary;
            _viewModel.Status = readiness.IsReady ? "Public alpha gate passed." : "Public alpha gate has blocking findings.";
        }
        catch (OperationCanceledException)
        {
            _viewModel.Status = "Public alpha gate cancelled.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            _viewModel.Status = $"Public alpha gate failed: {ex.GetType().Name}: {ex.Message}";
            _viewModel.Findings.Add(new FindingRow("High", "public-alpha", "PUBLIC_ALPHA_RUN_FAILED", ex.Message));
        }
        finally
        {
            _viewModel.IsBusy = false;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void ExportEvidence_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var folder = string.IsNullOrWhiteSpace(_viewModel.EvidenceFolder) ? ResolveDefaultEvidenceFolder() : _viewModel.EvidenceFolder;
        Directory.CreateDirectory(folder);
        _viewModel.EvidenceFolder = folder;
        _viewModel.Evidence.Clear();

        if (_viewModel.LastSclProfile is null && _viewModel.LastPublicAlphaReadinessProfile is null)
        {
            System.Windows.MessageBox.Show(this, "Run the workbench or public alpha gate before exporting evidence.", "No evidence", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        WriteEvidence(folder, "scl-engineering-profile", _viewModel.LastSclProfile?.ToMarkdown(), _viewModel.LastSclProfile, options);
        WriteEvidence(folder, "process-bus-binding-profile", _viewModel.LastBindingProfile?.ToMarkdown(), _viewModel.LastBindingProfile, options);
        WriteEvidence(folder, "goose-diagnostics-profile", _viewModel.LastGooseProfile?.ToMarkdown(), _viewModel.LastGooseProfile, options);
        WriteEvidence(folder, "sv-diagnostics-profile", _viewModel.LastSampledValuesProfile?.ToMarkdown(), _viewModel.LastSampledValuesProfile, options);
        WriteEvidence(folder, "mms-readonly-loopback-profile", _viewModel.LastMmsLoopbackProfile?.ToMarkdown(), _viewModel.LastMmsLoopbackProfile, options);
        WriteEvidence(folder, "public-alpha-readiness-profile", _viewModel.LastPublicAlphaReadinessProfile?.ToMarkdown(), _viewModel.LastPublicAlphaReadinessProfile, options);

        _viewModel.Status = $"Evidence exported to {folder}";
    }

    private void Clear_Click(object sender, System.Windows.RoutedEventArgs e)
        => _viewModel.ClearResults();

    private static PcapObservation ObservePcap(string sclPath, string pcapPath, double nominalFrequencyHz)
    {
        var document = new SclParser().Load(sclPath);
        var monitor = new ProcessBusStreamMonitor(document, nominalFrequencyHz <= 0 ? 50 : nominalFrequencyHz);
        var packets = PcapReader.ReadAll(pcapPath);
        var decoded = 0;
        var other = 0;
        foreach (var packet in packets)
        {
            var streamEvent = monitor.Observe(packet);
            if (streamEvent.Kind == ProcessBusEventKind.Unknown)
                other++;
            else
                decoded++;
        }

        return new PcapObservation(monitor.Summaries.ToArray(), packets.Count, decoded, other);
    }

    private void PopulateScl(SclEngineeringProfile profile)
    {
        foreach (var ln in profile.LogicalNodes)
        {
            _viewModel.LogicalNodes.Add(new SclNodeRow(
                ln.Reference,
                ln.LnClass,
                string.IsNullOrWhiteSpace(ln.LnType) ? "-" : ln.LnType,
                ln.DataSetCount,
                ln.ReportControlCount,
                ln.GooseControlCount,
                ln.SampledValueControlCount,
                ln.InputReferenceCount));
        }
    }

    private void PopulateBinding(ExpectedObservedBindingProfile profile)
    {
        foreach (var row in profile.Goose)
        {
            _viewModel.ProcessBusRows.Add(new ProcessBusRow(
                "GOOSE",
                TextOrDash(row.ExpectedControlBlockReference),
                row.MatchKind.ToString(),
                FormatAppId(row.ExpectedAppId, row.ObservedAppId),
                PairText(row.ExpectedDestinationMac, row.ObservedDestinationMac),
                FormatVlan(row.ExpectedVlanId, row.ObservedVlanId),
                PairNumber(row.ExpectedConfigurationRevision, row.ObservedConfigurationRevision),
                row.ObservedPacketCount,
                row.Findings.Count));
        }

        foreach (var row in profile.SampledValues)
        {
            _viewModel.ProcessBusRows.Add(new ProcessBusRow(
                "SV",
                TextOrDash(row.ExpectedControlBlockReference),
                row.MatchKind.ToString(),
                FormatAppId(row.ExpectedAppId, row.ObservedAppId),
                PairText(row.ExpectedDestinationMac, row.ObservedDestinationMac),
                FormatVlan(row.ExpectedVlanId, row.ObservedVlanId),
                PairNumber(row.ExpectedConfigurationRevision, row.ObservedConfigurationRevision),
                row.ObservedPacketCount,
                row.Findings.Count));
        }
    }

    private void PopulateGoose(GooseDiagnosticsProfile profile)
    {
        foreach (var row in profile.Streams)
        {
            _viewModel.GooseRows.Add(new GooseRow(
                row.Status.ToString(),
                TextOrDash(row.ExpectedControlBlockReference),
                TextOrDash(row.ObservedStreamId),
                FormatAppId(row.ObservedAppId ?? row.ExpectedAppId),
                row.ObservedPacketCount,
                row.LastStateNumber?.ToString(CultureInfo.InvariantCulture) ?? "-",
                row.LastSequenceNumber?.ToString(CultureInfo.InvariantCulture) ?? "-",
                row.SequenceGapCount,
                row.DuplicateCount,
                row.TimeoutCount,
                row.HealthScore));
        }
    }

    private void PopulateSampledValues(SampledValuesDiagnosticsProfile profile)
    {
        foreach (var row in profile.Streams)
        {
            _viewModel.SampledValuesRows.Add(new SampledValuesRow(
                row.Status.ToString(),
                TextOrDash(row.ExpectedControlBlockReference),
                TextOrDash(row.ObservedStreamId),
                FormatAppId(row.ObservedAppId ?? row.ExpectedAppId),
                row.ObservedPacketCount,
                FormatSampleRange(row.FirstSampleCount, row.LastSampleCount),
                row.SequenceGapCount,
                row.MissedSampleCount,
                row.DuplicateSampleCount,
                row.OutOfOrderSampleCount,
                row.LastSampleSynchronization?.ToString(CultureInfo.InvariantCulture) ?? "-",
                row.HealthScore));
        }
    }

    private void PopulateMms(MmsReadOnlyServerLoopbackProfile profile)
    {
        foreach (var gate in profile.Gates)
            _viewModel.MmsGates.Add(new MmsGateRow(gate.Name, gate.IsPass ? "PASS" : "FAIL", gate.Message));
    }

    private void PopulateMetrics(
        SclEngineeringProfile scl,
        ExpectedObservedBindingProfile binding,
        GooseDiagnosticsProfile goose,
        SampledValuesDiagnosticsProfile sv,
        MmsReadOnlyServerLoopbackProfile mms,
        int packetCount,
        int decodedCount)
    {
        _viewModel.Metrics.Clear();
        _viewModel.Metrics.Add(new MetricRow("IED", scl.Ieds.Count.ToString(CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new MetricRow("LN", scl.LogicalNodes.Count.ToString(CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new MetricRow("DataSet", scl.DataSetCount.ToString(CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new MetricRow("Reports", scl.ReportControlCount.ToString(CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new MetricRow("GOOSE", $"{goose.HealthyStreamCount}/{goose.ExpectedStreamCount}"));
        _viewModel.Metrics.Add(new MetricRow("SV", $"{sv.HealthyStreamCount}/{sv.ExpectedStreamCount}"));
        _viewModel.Metrics.Add(new MetricRow("Binding", binding.IsReady ? "ready" : "check"));
        _viewModel.Metrics.Add(new MetricRow("MMS", mms.IsReady ? "ready" : "check"));
        _viewModel.Metrics.Add(new MetricRow("Packets", packetCount.ToString(CultureInfo.InvariantCulture)));
        _viewModel.Metrics.Add(new MetricRow("Decoded", decodedCount.ToString(CultureInfo.InvariantCulture)));
    }

    private void PopulateFindings(
        SclEngineeringProfile scl,
        ExpectedObservedBindingProfile binding,
        GooseDiagnosticsProfile goose,
        SampledValuesDiagnosticsProfile sv,
        MmsReadOnlyServerLoopbackProfile mms,
        PublicAlphaReadinessProfile? readiness)
    {
        foreach (var finding in scl.Findings)
            _viewModel.Findings.Add(new FindingRow(finding.Severity, "SCL", finding.Code, finding.Message));
        foreach (var finding in binding.Findings)
            _viewModel.Findings.Add(new FindingRow(finding.Severity, "binding", finding.Code, finding.Message));
        foreach (var finding in goose.Findings)
            _viewModel.Findings.Add(new FindingRow(finding.Severity, "GOOSE", finding.Code, finding.Message, finding.Recommendation));
        foreach (var finding in sv.Findings)
            _viewModel.Findings.Add(new FindingRow(finding.Severity, "SV", finding.Code, finding.Message, finding.Recommendation));
        foreach (var finding in mms.Findings)
            _viewModel.Findings.Add(new FindingRow("Warning", "MMS", "MMS_LOOPBACK_FINDING", finding));
        if (readiness is not null)
        {
            foreach (var finding in readiness.Findings)
                _viewModel.Findings.Add(new FindingRow(finding.Severity, finding.Area, finding.Code, finding.Message, finding.Recommendation));
        }
        if (_viewModel.Findings.Count == 0)
            _viewModel.Findings.Add(new FindingRow("Info", "workbench", "WORKBENCH_NO_FINDINGS", "No blocking issue detected by the selected profiles."));
    }

    private static string BuildSummary(
        SclEngineeringProfile scl,
        ExpectedObservedBindingProfile binding,
        GooseDiagnosticsProfile goose,
        SampledValuesDiagnosticsProfile sv,
        MmsReadOnlyServerLoopbackProfile mms,
        int packetCount,
        int decodedCount)
        => $"SCL IED={scl.Ieds.Count}, LD={scl.LogicalDevices.Count}, LN={scl.LogicalNodes.Count}, DataSets={scl.DataSetCount}, reports={scl.ReportControlCount}; process-bus packets={packetCount}, decoded={decodedCount}; binding={(binding.IsReady ? "ready" : "check")}; GOOSE healthy={goose.HealthyStreamCount}/{goose.ExpectedStreamCount}; SV healthy={sv.HealthyStreamCount}/{sv.ExpectedStreamCount}; MMS loopback={(mms.IsReady ? "ready" : "check")}.";

    private void WriteEvidence(string folder, string name, string? markdown, object? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            _viewModel.Evidence.Add(new EvidenceRow(name, "skip", "profile not available"));
            return;
        }

        var markdownPath = Path.Combine(folder, name + ".md");
        var jsonPath = Path.Combine(folder, name + ".json");
        if (!string.IsNullOrWhiteSpace(markdown))
            File.WriteAllText(markdownPath, markdown);
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(value, options));
        _viewModel.Evidence.Add(new EvidenceRow(name, "written", markdownPath));
        _viewModel.Evidence.Add(new EvidenceRow(name + " JSON", "written", jsonPath));
    }

    private static string ResolveDefaultSclPath()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        var candidate = Path.Combine(root, "samples", "scl", "minimal-station.scd");
        return File.Exists(candidate) ? candidate : string.Empty;
    }

    private static string ResolveDefaultEvidenceFolder()
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        return Path.Combine(root, ".artifacts", "workbench");
    }

    private static string FindRepoRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ARIEC61850.sln")) || Directory.Exists(Path.Combine(directory.FullName, "samples")))
                return directory.FullName;
            directory = directory.Parent;
        }
        return Directory.GetCurrentDirectory();
    }

    private static string SafeDirectory(string path)
        => !string.IsNullOrWhiteSpace(path) && Directory.Exists(Path.GetDirectoryName(path))
            ? Path.GetDirectoryName(path)!
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private static string TextOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string PairText(string? expected, string? observed)
    {
        expected = TextOrDash(expected);
        observed = TextOrDash(observed);
        return string.Equals(expected, observed, StringComparison.OrdinalIgnoreCase) ? expected : $"{expected} / {observed}";
    }

    private static string FormatVlan(ushort? expected, ushort? observed)
        => expected == observed ? FormatNullable(expected) : $"{FormatNullable(expected)} / {FormatNullable(observed)}";

    private static string PairNumber(uint? expected, uint? observed)
        => expected == observed ? FormatNullable(expected) : $"{FormatNullable(expected)} / {FormatNullable(observed)}";

    private static string FormatAppId(ushort? value)
        => value.HasValue ? $"0x{value.Value:X4}" : "-";

    private static string FormatAppId(ushort? expected, ushort? observed)
        => expected == observed ? FormatAppId(expected) : $"{FormatAppId(expected)} / {FormatAppId(observed)}";

    private static string FormatNullable(ushort? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string FormatNullable(uint? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string FormatSampleRange(ushort? first, ushort? last)
        => first.HasValue || last.HasValue ? $"{first?.ToString(CultureInfo.InvariantCulture) ?? "-"}..{last?.ToString(CultureInfo.InvariantCulture) ?? "-"}" : "-";

    private sealed record PcapObservation(ProcessBusStreamSummary[] Summaries, int PacketCount, int DecodedFrameCount, int OtherFrameCount);
}
