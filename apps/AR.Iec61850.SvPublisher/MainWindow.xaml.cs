using System.Windows;
using AR.Iec61850.SvPublisher.Models;
using AR.Iec61850.SvPublisher.ViewModels;

namespace AR.Iec61850.SvPublisher;

public partial class MainWindow : Window
{
    private readonly SvPublisherViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new SvPublisherViewModel();
        DataContext = _viewModel;
    }

    private void OpenConfig_Click(object sender, RoutedEventArgs e)
    {
        new SvConfigWindow
        {
            Owner = this,
            DataContext = _viewModel
        }.ShowDialog();
    }

    private void ManualMode_Click(object sender, RoutedEventArgs e)
        => _viewModel.Mode = InjectionMode.Manual;

    private void RampSetup_Click(object sender, RoutedEventArgs e)
    {
        new RampSetupWindow
        {
            Owner = this,
            DataContext = _viewModel
        }.ShowDialog();
    }

    private void StateSequencer_Click(object sender, RoutedEventArgs e)
    {
        new StateSequencerWindow
        {
            Owner = this,
            DataContext = _viewModel
        }.ShowDialog();
    }
}
