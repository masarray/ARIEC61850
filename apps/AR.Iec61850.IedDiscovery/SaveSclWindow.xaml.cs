using System.Windows;
using AR.Iec61850.Scl.Export;

namespace AR.Iec61850.IedDiscovery;

public sealed class SaveSclDialogViewModel
{
    public SaveSclDialogViewModel(string iedName, SclSchemaProfile selectedProfile)
    {
        IedName = string.IsNullOrWhiteSpace(iedName) ? "LIVE_IED" : iedName;
        SchemaProfiles = SclSchemaProfiles.All;
        SelectedSchemaProfile = SclSchemaProfiles.Get(selectedProfile);
    }

    public string IedName { get; }
    public IReadOnlyList<SclSchemaProfileDescriptor> SchemaProfiles { get; }
    public SclSchemaProfileDescriptor SelectedSchemaProfile { get; set; }
}

public partial class SaveSclWindow : Window
{
    public SaveSclWindow(string iedName, SclSchemaProfile selectedProfile = SclSchemaProfile.Edition2V31)
    {
        InitializeComponent();
        DataContext = new SaveSclDialogViewModel(iedName, selectedProfile);
    }

    public SaveSclDialogViewModel ViewModel => (SaveSclDialogViewModel)DataContext;

    private void Save_Click(object sender, RoutedEventArgs e)
        => DialogResult = true;
}
