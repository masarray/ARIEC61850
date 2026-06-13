using System.Windows;
using AR.Iec61850.SvPublisher.Models;
using AR.Iec61850.SvPublisher.ViewModels;

namespace AR.Iec61850.SvPublisher;

public partial class RampSetupWindow : Window
{
    public RampSetupWindow()
    {
        InitializeComponent();
    }

    private void UseRamp_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SvPublisherViewModel viewModel)
            viewModel.Mode = InjectionMode.Ramp;

        DialogResult = true;
    }
}
