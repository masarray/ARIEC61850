using System.Windows;
using AR.Iec61850.SvPublisher.ViewModels;

namespace AR.Iec61850.SvPublisher;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new SvPublisherViewModel();
    }
}
