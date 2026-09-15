using System.ComponentModel;
using System.Windows.Threading;

namespace AR.Iec61850.IedDiscovery;

public partial class MainWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _viewModel.PropertyChanged += CanonicalRuntimeLifecycle_PropertyChanged;
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

    private async void SclAutoReporting_TimerTick(object? sender, EventArgs e)
    {
        if (!_viewModel.IsConnected || !_viewModel.IsOnline || _sclAutoReportMonitors.Count == 0)
            return;

        await ReceiveSclAutoReportMonitorsAsync().ConfigureAwait(true);
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

    private void CanonicalRuntimeLifecycle_Closed(object? sender, EventArgs e)
    {
        _sclAutoReportTimer?.Stop();
        if (_sclAutoReportTimer != null)
            _sclAutoReportTimer.Tick -= SclAutoReporting_TimerTick;
        _sclAutoReportTimer = null;

        _viewModel.PropertyChanged -= CanonicalRuntimeLifecycle_PropertyChanged;
        Closing -= SclAutoReporting_Closing;
        Closed -= CanonicalRuntimeLifecycle_Closed;
    }
}
