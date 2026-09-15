using System.ComponentModel;

namespace AR.Iec61850.IedDiscovery;

public partial class MainWindow
{
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _viewModel.PropertyChanged += CanonicalRuntimeLifecycle_PropertyChanged;
        Closed += CanonicalRuntimeLifecycle_Closed;
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

    private void CanonicalRuntimeLifecycle_Closed(object? sender, EventArgs e)
    {
        _viewModel.PropertyChanged -= CanonicalRuntimeLifecycle_PropertyChanged;
        Closed -= CanonicalRuntimeLifecycle_Closed;
    }
}
