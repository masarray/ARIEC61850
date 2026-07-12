using System.Collections.ObjectModel;
using System.Windows;
using AR.Iec61850.IedDiscovery.ViewModels;

namespace AR.Iec61850.IedDiscovery;

public sealed class DiscoverIedDialogViewModel : ObservableObject
{
    private string _host = "192.168.1.10";
    private int _port = 102;
    private string _name = "IED";
    private int _timeoutMs = 30000;
    private string _apTitle = "1,1,1,999,1";
    private string _aeQualifier = "12";
    private string _pSelector = "00 00 00 01";
    private string _sSelector = "00 01";
    private string _tSelector = "00 01";
    private bool _readDataSetDirectories = true;
    private bool _readFileDirectory = true;
    private bool _probeReportAttributes = true;
    private ConnectionProfileRow? _selectedPreviousConnection;

    public string Host { get => _host; set => SetProperty(ref _host, value); }
    public int Port { get => _port; set => SetProperty(ref _port, value); }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public int TimeoutMs { get => _timeoutMs; set => SetProperty(ref _timeoutMs, value); }
    public string ApTitle { get => _apTitle; set => SetProperty(ref _apTitle, value); }
    public string AeQualifier { get => _aeQualifier; set => SetProperty(ref _aeQualifier, value); }
    public string PSelector { get => _pSelector; set => SetProperty(ref _pSelector, value); }
    public string SSelector { get => _sSelector; set => SetProperty(ref _sSelector, value); }
    public string TSelector { get => _tSelector; set => SetProperty(ref _tSelector, value); }
    public bool ReadDataSetDirectories { get => _readDataSetDirectories; set => SetProperty(ref _readDataSetDirectories, value); }
    public bool ReadFileDirectory { get => _readFileDirectory; set => SetProperty(ref _readFileDirectory, value); }
    public bool ProbeReportAttributes { get => _probeReportAttributes; set => SetProperty(ref _probeReportAttributes, value); }
    public ObservableCollection<ConnectionProfileRow> PreviousConnections { get; } = new();

    public ConnectionProfileRow? SelectedPreviousConnection
    {
        get => _selectedPreviousConnection;
        set
        {
            if (!SetProperty(ref _selectedPreviousConnection, value) || value == null)
                return;
            Host = value.Host;
            Port = value.Port;
            Name = value.Name;
            TimeoutMs = value.TimeoutMs;
        }
    }
}

public partial class DiscoverIedWindow : Window
{
    public DiscoverIedWindow(DiscoverIedDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public DiscoverIedDialogViewModel ViewModel => (DiscoverIedDialogViewModel)DataContext;

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ConnectionProfileStore.Save(new ConnectionProfileRow(ViewModel.Host, ViewModel.Port, ViewModel.Name, ViewModel.TimeoutMs));
        ReloadProfiles();
    }

    private void Discover_Click(object sender, RoutedEventArgs e)
    {
        // Recent connection is saved after successful discovery with the resolved IED identity.
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void PreviousConnections_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedPreviousConnection != null)
            DialogResult = true;
    }

    private void ReloadProfiles()
    {
        ViewModel.PreviousConnections.Clear();
        foreach (var profile in ConnectionProfileStore.Load())
            ViewModel.PreviousConnections.Add(profile);
    }
}
