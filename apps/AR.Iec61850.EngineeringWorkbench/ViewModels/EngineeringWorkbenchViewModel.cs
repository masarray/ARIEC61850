using System.Collections.ObjectModel;
using AR.Iec61850.Diagnostics.Binding;
using AR.Iec61850.Diagnostics.Goose;
using AR.Iec61850.Diagnostics.SampledValues;
using AR.Iec61850.EngineeringWorkbench.Models;
using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Simulation;

namespace AR.Iec61850.EngineeringWorkbench.ViewModels;

public sealed class EngineeringWorkbenchViewModel : ObservableObject
{
    private string _sclPath = string.Empty;
    private string _pcapPath = string.Empty;
    private string _evidenceFolder = string.Empty;
    private string _status = "Open an SCL file, optionally add a PCAP, then run the workbench.";
    private string _summary = "No profile loaded yet.";
    private string _activeTabHint = "Read-only engineering harness. Engine logic stays in src; this app only orchestrates profile builders.";
    private bool _isBusy;
    private double _nominalFrequencyHz = 50;

    public string SclPath
    {
        get => _sclPath;
        set => SetProperty(ref _sclPath, value);
    }

    public string PcapPath
    {
        get => _pcapPath;
        set => SetProperty(ref _pcapPath, value);
    }

    public string EvidenceFolder
    {
        get => _evidenceFolder;
        set => SetProperty(ref _evidenceFolder, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string Summary
    {
        get => _summary;
        set => SetProperty(ref _summary, value);
    }

    public string ActiveTabHint
    {
        get => _activeTabHint;
        set => SetProperty(ref _activeTabHint, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public double NominalFrequencyHz
    {
        get => _nominalFrequencyHz;
        set => SetProperty(ref _nominalFrequencyHz, value <= 0 ? 50 : value);
    }

    public ObservableCollection<MetricRow> Metrics { get; } = new();
    public ObservableCollection<FindingRow> Findings { get; } = new();
    public ObservableCollection<SclNodeRow> LogicalNodes { get; } = new();
    public ObservableCollection<ProcessBusRow> ProcessBusRows { get; } = new();
    public ObservableCollection<GooseRow> GooseRows { get; } = new();
    public ObservableCollection<SampledValuesRow> SampledValuesRows { get; } = new();
    public ObservableCollection<MmsGateRow> MmsGates { get; } = new();
    public ObservableCollection<EvidenceRow> Evidence { get; } = new();

    public SclEngineeringProfile? LastSclProfile { get; set; }
    public ExpectedObservedBindingProfile? LastBindingProfile { get; set; }
    public GooseDiagnosticsProfile? LastGooseProfile { get; set; }
    public SampledValuesDiagnosticsProfile? LastSampledValuesProfile { get; set; }
    public MmsReadOnlyServerLoopbackProfile? LastMmsLoopbackProfile { get; set; }
    public PublicAlphaReadinessProfile? LastPublicAlphaReadinessProfile { get; set; }

    public void ClearResults()
    {
        Metrics.Clear();
        Findings.Clear();
        LogicalNodes.Clear();
        ProcessBusRows.Clear();
        GooseRows.Clear();
        SampledValuesRows.Clear();
        MmsGates.Clear();
        Evidence.Clear();
        LastSclProfile = null;
        LastBindingProfile = null;
        LastGooseProfile = null;
        LastSampledValuesProfile = null;
        LastMmsLoopbackProfile = null;
        LastPublicAlphaReadinessProfile = null;
        Summary = "Profiles cleared.";
    }
}
