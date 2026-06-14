using System.Collections.ObjectModel;
using System.Windows;
using AR.Iec61850.Discovery;
using AR.Iec61850.IedDiscovery.ViewModels;
using AR.Iec61850.Mms;

namespace AR.Iec61850.IedDiscovery;

public partial class EnableReportWindow : Window
{
    public EnableReportWindow(EnableReportDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public EnableReportDialogViewModel ViewModel => (EnableReportDialogViewModel)DataContext;

    private void Default_Click(object sender, RoutedEventArgs e)
        => ViewModel.ApplyDefaults();

    private void Validate_Click(object sender, RoutedEventArgs e)
        => ViewModel.Validate();

    private void Enable_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.Validate())
            return;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}

public sealed class EnableReportDialogViewModel : ObservableObject
{
    private string _selectedDataSet = string.Empty;
    private string _reportId = string.Empty;
    private string _integrityPeriodMs = "0";
    private string _validationMessage = "Validate before enabling. Guarded enable writes RptEna and cleans up after the monitor window.";
    private bool _performGeneralInterrogation;
    private readonly bool _isDynamicSlot;

    public EnableReportDialogViewModel(LiveIedReportControlModel rcb, IEnumerable<string> dataSets)
        : this(ToCandidate(rcb), dataSets)
    {
    }

    public EnableReportDialogViewModel(MmsReportControlCandidate rcb, IEnumerable<string> dataSets)
    {
        _isDynamicSlot = string.IsNullOrWhiteSpace(rcb.DataSetReference);
        ReportReference = rcb.Reference;
        ReportId = string.IsNullOrWhiteSpace(rcb.ReportId) ? rcb.Reference : rcb.ReportId;
        SelectedDataSet = _isDynamicSlot ? "<dynamic: temporary DataSet from pinned/priority signals>" : rcb.DataSetReference;
        var reservation = rcb.Buffered ? $"ResvTms={TextOrDash(rcb.ReservationTimeSeconds)}" : $"Resv={TextOrDash(rcb.ReservationState)}";
        CurrentState = $"RptEna={TextOrDash(rcb.EnabledState)}, {reservation}, DatSet={TextOrDash(rcb.DataSetReference)}, ConfRev={TextOrDash(rcb.ConfRev)}, BufTm={TextOrDash(rcb.BufferTimeMs)} ms";
        ModeText = (rcb.Buffered ? "Buffered" : "Unbuffered") + (_isDynamicSlot ? " dynamic report slot" : " static report control block");
        if (_isDynamicSlot)
            DataSets.Add(SelectedDataSet);
        foreach (var ds in dataSets.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            DataSets.Add(ds);
        if (!string.IsNullOrWhiteSpace(SelectedDataSet) && !DataSets.Contains(SelectedDataSet))
            DataSets.Insert(0, SelectedDataSet);
        ApplyDefaults();
    }

    private static MmsReportControlCandidate ToCandidate(LiveIedReportControlModel rcb)
        => new()
        {
            Domain = rcb.Domain,
            LogicalNode = rcb.LogicalNode,
            FunctionalConstraint = rcb.Buffered ? "BR" : "RP",
            Name = rcb.Name,
            Reference = rcb.Reference,
            Buffered = rcb.Buffered,
            DataSetReference = rcb.DataSetReference,
            ReportId = rcb.ReportId,
            ConfRev = rcb.ConfRev,
            TriggerOptions = rcb.TriggerOptions,
            OptionalFields = rcb.OptionalFields,
            BufferTimeMs = rcb.BufferTimeMs,
            IntegrityPeriodMs = rcb.IntegrityPeriodMs,
            EnabledState = rcb.EnabledState,
            ReservationState = rcb.ReservationState,
            ReservationTimeSeconds = rcb.ReservationTimeSeconds,
            Status = rcb.Status
        };

    public string ReportReference { get; }
    public ObservableCollection<string> DataSets { get; } = new();
    public string CurrentState { get; }
    public string ModeText { get; }
    public string ReportId { get => _reportId; set => SetProperty(ref _reportId, value); }
    public string SelectedDataSet { get => _selectedDataSet; set { if (SetProperty(ref _selectedDataSet, value)) OnPropertyChanged(nameof(CanEnable)); } }
    public string IntegrityPeriodMs { get => _integrityPeriodMs; set => SetProperty(ref _integrityPeriodMs, value); }
    public string ValidationMessage { get => _validationMessage; set => SetProperty(ref _validationMessage, value); }
    public bool PerformGeneralInterrogation { get => _performGeneralInterrogation; set => SetProperty(ref _performGeneralInterrogation, value); }
    public bool IsDynamicSlot => _isDynamicSlot;
    public bool CanEnable => _isDynamicSlot || !string.IsNullOrWhiteSpace(SelectedDataSet);

    public bool TrgDataChange { get; set; }
    public bool TrgQualityChange { get; set; }
    public bool TrgDataUpdate { get; set; }
    public bool TrgIntegrity { get; set; }
    public bool TrgGeneralInterrogation { get; set; }
    public bool OptSequenceNumber { get; set; }
    public bool OptTimeOfEntry { get; set; }
    public bool OptReasonForInclusion { get; set; }
    public bool OptDataSetName { get; set; }
    public bool OptDataReference { get; set; }
    public bool OptBufferOverflow { get; set; }
    public bool OptEntryId { get; set; }
    public bool OptConfRev { get; set; }

    public void ApplyDefaults()
    {
        TrgDataChange = true;
        TrgQualityChange = true;
        TrgDataUpdate = true;
        TrgIntegrity = true;
        TrgGeneralInterrogation = true;
        OptSequenceNumber = true;
        OptTimeOfEntry = true;
        OptReasonForInclusion = true;
        OptDataSetName = true;
        OptDataReference = false;
        OptBufferOverflow = true;
        OptEntryId = false;
        OptConfRev = true;
        PerformGeneralInterrogation = false;
        IntegrityPeriodMs = "0";
        RaiseOptionChanges();
        ValidationMessage = "Default trigger/options loaded. Review DataSet before enabling.";
    }

    public bool Validate()
    {
        if (!_isDynamicSlot && string.IsNullOrWhiteSpace(SelectedDataSet))
        {
            ValidationMessage = "Select a DataSet before enabling this report.";
            return false;
        }

        if (!int.TryParse(IntegrityPeriodMs, out var integrity) || integrity < 0)
        {
            ValidationMessage = "Integrity period must be a positive integer or 0.";
            return false;
        }

        ValidationMessage = _isDynamicSlot
            ? "Ready. Enable creates a temporary dynamic DataSet from pinned/priority signals, binds the RCB, then starts guarded monitoring."
            : "Ready. Enable starts a guarded report monitor and writes RptEna only after validation.";
        return true;
    }

    private void RaiseOptionChanges()
    {
        OnPropertyChanged(nameof(TrgDataChange));
        OnPropertyChanged(nameof(TrgQualityChange));
        OnPropertyChanged(nameof(TrgDataUpdate));
        OnPropertyChanged(nameof(TrgIntegrity));
        OnPropertyChanged(nameof(TrgGeneralInterrogation));
        OnPropertyChanged(nameof(OptSequenceNumber));
        OnPropertyChanged(nameof(OptTimeOfEntry));
        OnPropertyChanged(nameof(OptReasonForInclusion));
        OnPropertyChanged(nameof(OptDataSetName));
        OnPropertyChanged(nameof(OptDataReference));
        OnPropertyChanged(nameof(OptBufferOverflow));
        OnPropertyChanged(nameof(OptEntryId));
        OnPropertyChanged(nameof(OptConfRev));
        OnPropertyChanged(nameof(CanEnable));
    }

    private static string TextOrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;
}
