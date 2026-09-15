using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AR.Iec61850.Engineering.Runtime;
using AR.Iec61850.IedDiscovery.ViewModels;
using AR.Iec61850.Mms;
using Microsoft.Win32;

namespace AR.Iec61850.IedDiscovery;

public partial class MainWindow
{
    private readonly CanonicalRuntimeSnapshotPublisher _canonicalRuntimePublisher = new();
    private bool _canonicalRuntimeHooksInstalled;
    private bool _canonicalRuntimeTickInProgress;
    private long _lastPresentedCanonicalModelGeneration = -1;
    private long _lastPresentedCanonicalValueGeneration = -1;
    private string _lastPresentedMonitorSelection = string.Empty;
    private string _lastPresentedDetailSelection = string.Empty;
    private bool _monitorSelectionLimitReported;
    private bool _detailSelectionLimitReported;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_canonicalRuntimeHooksInstalled)
            return;

        _canonicalRuntimeHooksInstalled = true;
        _viewModel.PropertyChanged += CanonicalRuntimeViewModel_PropertyChanged;
        Closed += CanonicalRuntimeWindow_Closed;

        // P1B owns all recurring value refresh. The legacy timer remains compiled as a
        // rollback reference but is detached so report/poll responses can no longer write
        // directly into presentation rows.
        _monitorTimer.Tick -= MonitorTimer_Tick;
        _monitorTimer.Tick += CanonicalRuntimeMonitorTimer_Tick;

        // Replace the two value-consumer toolbar actions without rewriting the XAML shell.
        // Static topology/RCB engineering actions intentionally remain on their established
        // paths; live value reads and exports are canonical-runtime consumers from here on.
        RewireToolbarAction("Read", Read_Click, CanonicalRuntimeRead_Click);
        RewireToolbarAction("Export", Export_Click, CanonicalRuntimeExport_Click);

        if (_viewModel.LastDocument is { } document)
            QueueCanonicalRuntimeModel(document);

        _viewModel.AddStatus(
            "Info",
            "CANONICAL_RUNTIME_CUTOVER",
            "P1B active: manual reads, polling, reports, visible value rows, pinned monitor rows, and runtime export use the canonical runtime source.");
    }

    private async void CanonicalRuntimeWindow_Closed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= CanonicalRuntimeViewModel_PropertyChanged;
        _monitorTimer.Tick -= CanonicalRuntimeMonitorTimer_Tick;
        Closed -= CanonicalRuntimeWindow_Closed;
        await _canonicalRuntimePublisher.DisposeAsync().ConfigureAwait(true);
    }

    private void CanonicalRuntimeViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(IedDiscoveryViewModel.LastDocument), StringComparison.Ordinal))
            return;

        if (_viewModel.LastDocument is { } document)
            QueueCanonicalRuntimeModel(document);
        else
            ResetCanonicalPresentationGeneration();
    }

    private void QueueCanonicalRuntimeModel(Discovery.LiveIedModelDiscoveryDocument document)
    {
        if (CanonicalRuntimeIngressPublication.TryPublishLiveDiscovery(_canonicalRuntimePublisher, document))
        {
            ResetCanonicalPresentationGeneration();
            _viewModel.AddStatus(
                "Info",
                "CANONICAL_MODEL_QUEUED",
                $"Queued immutable canonical runtime model for {document.IedName}; live values will bind only after that generation is published.");
        }
        else
        {
            _viewModel.AddStatus(
                "Warning",
                "CANONICAL_MODEL_REJECTED",
                "Canonical runtime publisher rejected the model because its bounded worker is stopping or unavailable.");
        }
    }

    private void ResetCanonicalPresentationGeneration()
    {
        _lastPresentedCanonicalModelGeneration = -1;
        _lastPresentedCanonicalValueGeneration = -1;
        _lastPresentedMonitorSelection = string.Empty;
        _lastPresentedDetailSelection = string.Empty;
        _monitorSelectionLimitReported = false;
        _detailSelectionLimitReported = false;
    }

    private async Task<bool> EnsureCanonicalRuntimeReadyAsync(CancellationToken cancellationToken)
    {
        if (_canonicalRuntimePublisher.Current is not null)
            return true;

        if (_viewModel.LastDocument is { } document)
            QueueCanonicalRuntimeModel(document);

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (_canonicalRuntimePublisher.Current is null && DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken).ConfigureAwait(true);
        }

        return _canonicalRuntimePublisher.Current is not null;
    }

    private async void CanonicalRuntimeRead_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSession == null || !_viewModel.IsConnected || !_viewModel.IsOnline)
        {
            _viewModel.AddStatus("Warning", "READ_NOT_ONLINE", "Manual read requires an active online MMS session. Use Discover IED and keep Online enabled.");
            return;
        }

        var selectedRows = BuildSmartReadableRowsForSelection().Take(128).ToArray();
        if (selectedRows.Length == 0)
        {
            _viewModel.AddStatus("Warning", "READ_NO_TARGET", "Select a DO or DA row before reading.");
            return;
        }

        _viewModel.IsBusy = true;
        _cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(5000, _viewModel.TimeoutMs)));
        try
        {
            if (!await EnsureCanonicalRuntimeReadyAsync(_cancellation.Token).ConfigureAwait(true))
            {
                _viewModel.AddStatus("Error", "CANONICAL_RUNTIME_NOT_READY", "Canonical runtime model was not published before the manual-read deadline.");
                return;
            }

            var applied = 0;
            var unresolved = 0;
            foreach (var target in selectedRows)
            {
                if (string.IsNullOrWhiteSpace(target.Reference) || string.Equals(target.Reference, "-", StringComparison.Ordinal))
                    continue;

                target.Status = "reading";
                var reference = MmsObjectReference.Parse(target.Reference, target.Fc);
                var result = await _activeSession.ReadSingleVariableAsync(reference, _cancellation.Token).ConfigureAwait(true);
                if (!result.IsSuccess)
                {
                    target.Status = "failed";
                    unresolved++;
                    continue;
                }

                var projection = CanonicalMmsRuntimePublisherAdapter.ApplyRead(
                    _canonicalRuntimePublisher,
                    target.Reference,
                    target.Fc,
                    result,
                    "manual-read");
                applied += projection.AppliedSignalCount;
                unresolved += projection.UnresolvedUpdateCount;
                target.Status = projection.IsComplete ? "read" : "unresolved";
            }

            RefreshCanonicalPresentation(DateTimeOffset.Now, force: true);
            _viewModel.AddStatus(
                unresolved == 0 ? "Info" : "Warning",
                "READ_COMPLETE",
                $"Manual read completed through canonical runtime: targets={selectedRows.Length}, appliedSignals={applied}, unresolved={unresolved}.");
        }
        catch (OperationCanceledException)
        {
            _viewModel.AddStatus("Warning", "READ_CANCELLED", "Manual read cancelled.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            _viewModel.AddStatus("Error", "READ_FAILED", $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _viewModel.IsBusy = false;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void CanonicalRuntimeExport_Click(object sender, RoutedEventArgs e)
    {
        var current = _canonicalRuntimePublisher.Current;
        if (current is null)
        {
            MessageBox.Show(this, "Run live discovery or open an SCL file first and wait for the canonical model to publish.", "No canonical runtime model", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export canonical IEC 61850 runtime values",
            Filter = "Canonical runtime CSV (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"iec61850-runtime-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.csv"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            using var writer = new StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8);
            var result = CanonicalRuntimeCsvExporter.Write(current.Values, writer);
            _viewModel.AddStatus(
                result.ValuesChangedDuringExport ? "Warning" : "Info",
                "CANONICAL_RUNTIME_EXPORTED",
                result.Summary);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _viewModel.AddStatus("Error", "CANONICAL_RUNTIME_EXPORT_FAILED", $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async void CanonicalRuntimeMonitorTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.Now;
        RefreshCanonicalPresentation(now, force: false);

        if (_canonicalRuntimeTickInProgress || !_viewModel.IsConnected || !_viewModel.IsOnline || _activeSession == null)
            return;

        _canonicalRuntimeTickInProgress = true;
        try
        {
            if (!await EnsureCanonicalRuntimeReadyAsync(CancellationToken.None).ConfigureAwait(true))
                return;

            if (_activeReportMonitor != null)
            {
                await ReceiveCanonicalReportSliceAsync(_activeReportMonitor).ConfigureAwait(true);
            }
            else if (_viewModel.MonitorSignals.Count > 0)
            {
                await PollCanonicalPinnedSignalsAsync().ConfigureAwait(true);
            }

            RefreshCanonicalPresentation(DateTimeOffset.Now, force: false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            _viewModel.AddStatus("Warning", "CANONICAL_RUNTIME_REFRESH_FAILED", $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _canonicalRuntimeTickInProgress = false;
        }
    }

    private async Task ReceiveCanonicalReportSliceAsync(MmsPersistentReportMonitorSession activeMonitor)
    {
        if (_activeSession == null)
            return;

        var reportCoveredReferences = activeMonitor.Plan.Members
            .Select(x => x.UserReference)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .SelectMany(ReportCoverageReferences)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fallbackPollReferences = _viewModel.MonitorSignals
            .Where(x => !string.IsNullOrWhiteSpace(x.Reference) && !reportCoveredReferences.Contains(x.Reference))
            .Select(x => x.Reference)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        var monitor = await _activeSession.ReceivePersistentReportMonitorSliceAsync(
            activeMonitor,
            TimeSpan.FromMilliseconds(300),
            pollDirectory: _lastDiscovery?.IedDirectory,
            pollReferences: fallbackPollReferences,
            pollInterval: fallbackPollReferences.Length == 0 ? null : TimeSpan.FromSeconds(1),
            triggerGeneralInterrogation: false,
            cancellationToken: CancellationToken.None).ConfigureAwait(true);

        var appliedSignals = 0;
        var unresolved = 0;
        foreach (var report in monitor.Reports)
        {
            var projection = MmsReportValueProjector.Project(report);
            foreach (var warning in projection.Warnings.Take(4))
                _viewModel.AddStatus("Warning", "REPORT_PROJECTOR", warning);

            var result = CanonicalMmsRuntimePublisherAdapter.ApplyReportProjection(_canonicalRuntimePublisher, projection);
            appliedSignals += result.AppliedSignalCount;
            unresolved += result.UnresolvedUpdateCount;
        }

        foreach (var poll in monitor.PollReads)
        {
            var result = CanonicalMmsRuntimeApplicationAdapter.ApplyPollRead(_canonicalRuntimePublisher, poll, "poll");
            appliedSignals += result.AppliedSignalCount;
            unresolved += result.UnresolvedUpdateCount;
            if (!poll.IsSuccess)
                MarkMonitorStatus(poll.SelectedReference, poll.FunctionalConstraint, poll.Message);
        }

        if (monitor.Reports.Count > 0)
        {
            _viewModel.AddStatus(
                unresolved == 0 ? "Info" : "Warning",
                "REPORT_RECEIVED",
                $"Received {monitor.Reports.Count} report frame(s); canonical appliedSignals={appliedSignals}, unresolved={unresolved}, monitorTotal={activeMonitor.ReportCount}.");
        }
    }

    private async Task PollCanonicalPinnedSignalsAsync()
    {
        if (_activeSession == null)
            return;

        foreach (var signal in _viewModel.MonitorSignals.Take(16).ToArray())
        {
            if (string.IsNullOrWhiteSpace(signal.Reference) || string.IsNullOrWhiteSpace(signal.FunctionalConstraint))
                continue;

            try
            {
                var read = await _activeSession.ReadSingleVariableAsync(
                    MmsObjectReference.Parse(signal.Reference, signal.FunctionalConstraint),
                    CancellationToken.None).ConfigureAwait(true);

                if (read.IsSuccess)
                {
                    var projection = CanonicalMmsRuntimePublisherAdapter.ApplyRead(
                        _canonicalRuntimePublisher,
                        signal.Reference,
                        signal.FunctionalConstraint,
                        read,
                        "polling");
                    signal.Status = projection.IsComplete ? "live" : "unresolved";
                }
                else
                {
                    // Preserve the last good canonical value; only presentation status is
                    // changed for a failed transport/protocol read.
                    signal.Status = "failed";
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException)
            {
                signal.Status = $"failed: {ex.Message}";
            }
        }
    }

    private void RefreshCanonicalPresentation(DateTimeOffset now, bool force)
    {
        foreach (var signal in _viewModel.MonitorSignals)
            signal.RefreshAge(now);

        var current = _canonicalRuntimePublisher.Current;
        if (current is null)
            return;

        if (current.ModelGeneration != _lastPresentedCanonicalModelGeneration)
        {
            _lastPresentedCanonicalModelGeneration = current.ModelGeneration;
            _lastPresentedCanonicalValueGeneration = -1;
            _lastPresentedMonitorSelection = string.Empty;
            _lastPresentedDetailSelection = string.Empty;
            _viewModel.AddStatus(
                "Info",
                "CANONICAL_MODEL_PUBLISHED",
                $"Canonical model generation {current.ModelGeneration} published with {current.ModelSnapshot.Model.SignalCount} signal(s). Runtime values start empty for this generation.");
        }

        var valueGeneration = current.Values.ValueGeneration;
        var monitorSelections = _viewModel.MonitorSignals
            .Where(x => !string.IsNullOrWhiteSpace(x.Reference))
            .Select(x => new CanonicalRuntimeSignalSelection(x.Reference, x.FunctionalConstraint))
            .ToArray();
        var monitorSignature = SelectionSignature(monitorSelections);

        var detailSelections = _viewModel.DetailRows
            .Where(IsReadableRow)
            .Select(x => new CanonicalRuntimeSignalSelection(x.Reference, x.Fc))
            .Take(CanonicalRuntimeApplicationProjection.MaximumUiSelectionCount + 1)
            .ToArray();
        var detailSignature = SelectionSignature(detailSelections);

        var runtimeChanged = valueGeneration != _lastPresentedCanonicalValueGeneration;
        if (force || runtimeChanged || !string.Equals(monitorSignature, _lastPresentedMonitorSelection, StringComparison.Ordinal))
            RefreshCanonicalMonitorRows(monitorSelections, now);

        if (force || runtimeChanged || !string.Equals(detailSignature, _lastPresentedDetailSelection, StringComparison.Ordinal))
            RefreshCanonicalDetailRows(detailSelections);

        _lastPresentedCanonicalValueGeneration = valueGeneration;
        _lastPresentedMonitorSelection = monitorSignature;
        _lastPresentedDetailSelection = detailSignature;
    }

    private void RefreshCanonicalMonitorRows(
        IReadOnlyList<CanonicalRuntimeSignalSelection> selections,
        DateTimeOffset now)
    {
        if (selections.Count == 0)
            return;

        var projection = CanonicalRuntimeApplicationProjection.ForMonitor(_canonicalRuntimePublisher, selections);
        if (projection.WasTruncated && !_monitorSelectionLimitReported)
        {
            _monitorSelectionLimitReported = true;
            _viewModel.AddStatus(
                "Warning",
                "MONITOR_SELECTION_CAPPED",
                $"Pinned monitor projection is capped at {CanonicalRuntimeApplicationProjection.MaximumMonitorSelectionCount} canonical signals.");
        }

        var rows = projection.Rows.ToDictionary(
            row => SelectionKey(row.Reference, row.FunctionalConstraint),
            row => row,
            StringComparer.Ordinal);

        foreach (var signal in _viewModel.MonitorSignals)
        {
            if (!rows.TryGetValue(SelectionKey(signal.Reference, signal.FunctionalConstraint), out var row))
                continue;

            if (row.HasValue)
                signal.Value = row.Value;
            if (row.HasQuality)
                signal.Quality = row.Quality;
            signal.Source = string.IsNullOrWhiteSpace(row.Source) ? signal.Source : row.Source;
            signal.Status = row.HasReason && !string.IsNullOrWhiteSpace(row.Reason) ? row.Reason : "live";

            var updatedAt = row.UpdatedAtUtc == default ? now : row.UpdatedAtUtc;
            signal.MarkUpdated(updatedAt);
            if (row.HasTimestamp && !string.IsNullOrWhiteSpace(row.Timestamp))
                signal.Timestamp = row.Timestamp;
        }
    }

    private void RefreshCanonicalDetailRows(IReadOnlyList<CanonicalRuntimeSignalSelection> selections)
    {
        if (selections.Count == 0)
            return;

        var projection = CanonicalRuntimeApplicationProjection.ForUi(_canonicalRuntimePublisher, selections);
        if (projection.WasTruncated && !_detailSelectionLimitReported)
        {
            _detailSelectionLimitReported = true;
            _viewModel.AddStatus(
                "Warning",
                "DETAIL_SELECTION_CAPPED",
                $"Visible detail projection is capped at {CanonicalRuntimeApplicationProjection.MaximumUiSelectionCount} canonical signals.");
        }

        var rows = projection.Rows.ToDictionary(
            row => SelectionKey(row.Reference, row.FunctionalConstraint),
            row => row,
            StringComparer.Ordinal);

        foreach (var detail in _viewModel.DetailRows)
        {
            if (!rows.TryGetValue(SelectionKey(detail.Reference, detail.Fc), out var row))
                continue;

            if (row.HasValue)
                detail.Value = row.Value;
            if (row.HasQuality)
                detail.Quality = row.Quality;
            if (row.HasTimestamp)
                detail.Timestamp = row.Timestamp;
            detail.Status = row.HasReason && !string.IsNullOrWhiteSpace(row.Reason)
                ? row.Reason
                : string.IsNullOrWhiteSpace(row.Source) ? detail.Status : row.Source;
        }

        MmsValueDetailTreeBuilder.ApplySmartSummaries(_viewModel.DetailRootRows);
    }

    private void MarkMonitorStatus(string reference, string functionalConstraint, string status)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return;

        var row = _viewModel.MonitorSignals.FirstOrDefault(signal =>
            string.Equals(signal.Reference, reference, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(functionalConstraint) ||
             string.Equals(signal.FunctionalConstraint, functionalConstraint, StringComparison.OrdinalIgnoreCase)));
        if (row is not null)
            row.Status = string.IsNullOrWhiteSpace(status) ? "failed" : status;
    }

    private void RewireToolbarAction(string label, RoutedEventHandler legacy, RoutedEventHandler canonical)
    {
        var button = FindVisualChildren<Button>(this)
            .FirstOrDefault(candidate => string.Equals(FindButtonLabel(candidate), label, StringComparison.Ordinal));
        if (button is null)
        {
            _viewModel.AddStatus("Warning", "CANONICAL_TOOLBAR_REWIRE_MISSING", $"Could not locate toolbar action '{label}' for P1B cutover.");
            return;
        }

        button.Click -= legacy;
        button.Click += canonical;
    }

    private static string FindButtonLabel(Button button)
        => FindVisualChildren<TextBlock>(button)
            .Select(text => text.Text)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? string.Empty;

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null)
            yield break;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private static string SelectionSignature(IEnumerable<CanonicalRuntimeSignalSelection> selections)
        => string.Join("\u001E", selections.Select(selection => SelectionKey(selection.Reference, selection.FunctionalConstraint)));

    private static string SelectionKey(string reference, string functionalConstraint)
        => string.Concat(
            (reference ?? string.Empty).Trim(),
            "\u001F",
            (functionalConstraint ?? string.Empty).Trim().ToUpperInvariant());
}
