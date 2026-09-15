using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AR.Iec61850.IedDiscovery.ViewModels;

namespace AR.Iec61850.IedDiscovery;

public partial class MainWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _viewModel.PropertyChanged += CanonicalRuntimeLifecycle_PropertyChanged;
        _viewModel.StatusHistory.CollectionChanged += SclAutoReporting_StatusHistoryChanged;
        PreviewMouseLeftButtonDown += SclAutoReporting_PreviewMouseLeftButtonDown;
        PreviewKeyDown += SclAutoReporting_PreviewKeyDown;
        Closing += SclAutoReporting_Closing;
        Closed += CanonicalRuntimeLifecycle_Closed;

        _sclAutoReportTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _sclAutoReportTimer.Tick += SclAutoReporting_TimerTick;
        _sclAutoReportTimer.Start();
    }

    private void CanonicalRuntimeLifecycle_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(ViewModels.IedDiscoveryViewModel.LastDocument), StringComparison.Ordinal))
            return;

        if (_viewModel.LastDocument is null)
        {
            // Clear invalidates both the visible generation and any already accepted
            // in-flight publication. Closing/clearing one IED can therefore never leave
            // its values queryable or exportable while the application shows no model.
            _canonicalRuntimePublisher.Clear();
            ResetCanonicalPresentationGeneration();
        }
    }

    private void SclAutoReporting_StatusHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems == null)
            return;

        foreach (var row in e.NewItems.OfType<StatusHistoryRow>())
        {
            if (row.Code.Equals("SCL_OPENED", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(_openedSclPath))
            {
                // RunDiscoveryAsync currently clears _openedSclPath when an online
                // association starts. Preserve the explicitly opened SCL as the
                // connection/reporting context without changing offline Save-SCL state.
                _sclConnectionContextPath = _openedSclPath;
                continue;
            }

            if (row.Code.Equals("DISCOVERY_READY", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(_sclConnectionContextPath))
            {
                // Queue after the current discovery stack unwinds so IsBusy/finally
                // completes before automatic RCB writes start.
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => _ = TryStartSclAutoReportingAsync()));
                continue;
            }

            if (row.Code.Equals("IED_CLOSED", StringComparison.Ordinal))
                _sclConnectionContextPath = null;
        }
    }

    private async void SclAutoReporting_TimerTick(object? sender, EventArgs e)
    {
        if (!_viewModel.IsConnected || !_viewModel.IsOnline || _sclAutoReportMonitors.Count == 0)
            return;

        await ReceiveSclAutoReportMonitorsAsync().ConfigureAwait(true);
    }

    private async void SclAutoReporting_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_sclAutoReportMonitors.Count == 0)
            return;

        var button = FindVisualAncestor<Button>(e.OriginalSource as DependencyObject);
        if (button == null)
            return;

        var action = ResolveReportLifecycleToolbarAction(button);
        if (action == ReportLifecycleToolbarAction.None)
            return;

        e.Handled = true;
        await ExecuteReportLifecycleToolbarActionAsync(button, action).ConfigureAwait(true);
    }

    private async void SclAutoReporting_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_sclAutoReportMonitors.Count == 0 || e.Key is not (Key.Enter or Key.Space))
            return;

        var button = Keyboard.FocusedElement as Button;
        if (button == null)
            return;

        var action = ResolveReportLifecycleToolbarAction(button);
        if (action == ReportLifecycleToolbarAction.None)
            return;

        e.Handled = true;
        await ExecuteReportLifecycleToolbarActionAsync(button, action).ConfigureAwait(true);
    }

    private async Task ExecuteReportLifecycleToolbarActionAsync(Button button, ReportLifecycleToolbarAction action)
    {
        _sclAutoReportTimer?.Stop();

        if (action == ReportLifecycleToolbarAction.StopReports)
        {
            await StopAllReportMonitorsAsync("SCL_AUTO_STOP_REQUEST").ConfigureAwait(true);
            _sclAutoReportTimer?.Start();
            return;
        }

        await StopSclAutoReportMonitorsAsync(
            action == ReportLifecycleToolbarAction.Discover ? "SCL_AUTO_RECONNECT_CLEANUP" : "SCL_AUTO_CLOSE_CLEANUP").ConfigureAwait(true);

        _sclAutoReportTimer?.Start();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
    }

    private async void SclAutoReporting_Closing(object? sender, CancelEventArgs e)
    {
        if (_sclAutoWindowCloseReentry || _sclAutoReportMonitors.Count == 0)
            return;

        e.Cancel = true;
        _sclAutoReportTimer?.Stop();
        await StopSclAutoReportMonitorsAsync("SCL_AUTO_WINDOW_CLOSE_CLEANUP").ConfigureAwait(true);
        _sclAutoWindowCloseReentry = true;
        Close();
    }

    private static ReportLifecycleToolbarAction ResolveReportLifecycleToolbarAction(Button button)
    {
        if (VisualContainsText(button, "Discover"))
            return ReportLifecycleToolbarAction.Discover;
        if (VisualContainsText(button, "Close IED"))
            return ReportLifecycleToolbarAction.CloseIed;
        if (VisualContainsText(button, "Stop RCB"))
            return ReportLifecycleToolbarAction.StopReports;
        return ReportLifecycleToolbarAction.None;
    }

    private static bool VisualContainsText(DependencyObject root, string expected)
    {
        if (root is TextBlock textBlock && textBlock.Text.Equals(expected, StringComparison.OrdinalIgnoreCase))
            return true;

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            if (VisualContainsText(VisualTreeHelper.GetChild(root, index), expected))
                return true;
        }

        return false;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current != null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void CanonicalRuntimeLifecycle_Closed(object? sender, EventArgs e)
    {
        _sclAutoReportTimer?.Stop();
        if (_sclAutoReportTimer != null)
            _sclAutoReportTimer.Tick -= SclAutoReporting_TimerTick;
        _sclAutoReportTimer = null;

        _viewModel.PropertyChanged -= CanonicalRuntimeLifecycle_PropertyChanged;
        _viewModel.StatusHistory.CollectionChanged -= SclAutoReporting_StatusHistoryChanged;
        PreviewMouseLeftButtonDown -= SclAutoReporting_PreviewMouseLeftButtonDown;
        PreviewKeyDown -= SclAutoReporting_PreviewKeyDown;
        Closing -= SclAutoReporting_Closing;
        Closed -= CanonicalRuntimeLifecycle_Closed;
    }

    private enum ReportLifecycleToolbarAction
    {
        None,
        Discover,
        CloseIed,
        StopReports
    }
}
