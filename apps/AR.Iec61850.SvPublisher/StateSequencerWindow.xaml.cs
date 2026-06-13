using System.Windows;
using AR.Iec61850.SvPublisher.Models;
using AR.Iec61850.SvPublisher.ViewModels;

namespace AR.Iec61850.SvPublisher;

public partial class StateSequencerWindow : Window
{
    public StateSequencerWindow()
    {
        InitializeComponent();
    }

    private void UseSequencer_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SvPublisherViewModel viewModel)
            viewModel.Mode = InjectionMode.Sequencer;

        DialogResult = true;
    }
}
