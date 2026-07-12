using AR.Iec61850.Control;
using AR.Iec61850.Mms;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AR.Iec61850.IedDiscovery;

public partial class ControlWindow : Window
{
    private readonly MmsClientSession _mmsSession;
    private readonly string _objectReference;
    private readonly Iec61850ControlService _controlService = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly ObservableCollection<ControlEvidenceRow> _evidence = new();
    private Iec61850ControlObjectSession? _controlSession;
    private CancellationTokenSource? _operationCts;
    private Iec61850ControlStatusResult? _lastStatus;
    private Iec61850ControlValue? _primaryValue;
    private Iec61850ControlValue? _secondaryValue;
    private Iec61850ControlStatusState? _primaryExpectedState;
    private Iec61850ControlStatusState? _secondaryExpectedState;
    private string _primaryLabel = "OPEN";
    private string _secondaryLabel = "CLOSE";
    private bool _isBusy;

    public ControlWindow(MmsClientSession mmsSession, string objectReference)
    {
        _mmsSession = mmsSession ?? throw new ArgumentNullException(nameof(mmsSession));
        _objectReference = string.IsNullOrWhiteSpace(objectReference)
            ? throw new ArgumentException("Control object reference is empty.", nameof(objectReference))
            : objectReference;

        InitializeComponent();
        EvidenceGrid.ItemsSource = _evidence;
        ControlObjectText.Text = _objectReference;
        var originators = BuildOriginatorOptions();
        OriginatorCombo.ItemsSource = originators;
        OriginatorCombo.SelectedItem = originators.First(x => x.Category == Iec61850OriginCategory.StationControl);
        UpdateCommandButtons();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Detecting the live control model and exact MMS types...");
        AddEvidence("Discovery", "Started", _objectReference);
        try
        {
            _controlSession = await _controlService.OpenAsync(
                _mmsSession,
                _objectReference,
                _lifetimeCts.Token);

            ApplyDescriptor(_controlSession.Descriptor);
            AddEvidence(
                "Discovery",
                "Ready",
                $"{_controlSession.Descriptor.ControlModel}; ctlVal={_controlSession.Descriptor.CtlValSpecification.Signature}");
            await RefreshStatusCoreAsync(_lifetimeCts.Token);
            FooterStatusText.Text = _primaryValue != null && _secondaryValue != null
                ? $"Ready. Select {_primaryLabel} or {_secondaryLabel}; the engine handles Direct Operate or SBO automatically."
                : "The object was discovered, but no safe beginner command mapping is available for its ctlVal type.";
        }
        catch (OperationCanceledException)
        {
            FooterStatusText.Text = "Control initialization cancelled.";
            AddEvidence("Discovery", "Cancelled", "Initialization was cancelled.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            FooterStatusText.Text = "Control object is not command-ready.";
            StatusValueText.Text = "Unavailable";
            ApplyStatusAppearance(Iec61850ControlStatusState.Unknown);
            AddEvidence("Discovery", "Failed", ex.Message);
            TechnicalEvidenceTextBox.Text = ex.ToString();
            MessageBox.Show(
                this,
                $"The control object could not be opened safely.\n\n{ex.Message}",
                "Control not ready",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false, FooterStatusText.Text);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _operationCts?.Cancel();
        _lifetimeCts.Cancel();
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        var operationCts = _operationCts;
        _operationCts = null;
        operationCts?.Cancel();
        try
        {
            if (_controlSession != null)
            {
                await _controlSession.DisposeAsync();
                _controlSession = null;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            AddEvidence("Cleanup", "Warning", ex.Message);
        }
        finally
        {
            operationCts?.Dispose();
            _lifetimeCts.Dispose();
        }
    }

    private async void RefreshStatus_Click(object sender, RoutedEventArgs e)
    {
        if (_controlSession == null || _isBusy)
            return;

        SetBusy(true, "Reading live status value...");
        try
        {
            await RefreshStatusCoreAsync(_lifetimeCts.Token);
            FooterStatusText.Text = _lastStatus?.IsSuccess == true
                ? $"Live status: {_lastStatus.DisplayValue}."
                : "Status could not be confirmed.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or TimeoutException)
        {
            FooterStatusText.Text = "Status read failed.";
            AddEvidence("Status", "Exception", ex.Message);
        }
        finally
        {
            SetBusy(false, FooterStatusText.Text);
        }
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
        => await ExecuteCommandAsync(_primaryValue, _primaryExpectedState, _primaryLabel);

    private async void CloseCommand_Click(object sender, RoutedEventArgs e)
        => await ExecuteCommandAsync(_secondaryValue, _secondaryExpectedState, _secondaryLabel);

    private void StopCommand_Click(object sender, RoutedEventArgs e)
    {
        _operationCts?.Cancel();
        FooterStatusText.Text = "Cancelling the current client operation...";
        AddEvidence("Client", "Cancel", "Cancellation requested by user.");
    }

    private void CommandOption_Changed(object sender, RoutedEventArgs e)
    {
        ArmHintText.Text = LiveCommandArmCheckBox.IsChecked == true
            ? "Live command enabled. The IED still evaluates the selected checks."
            : "Live command is locked.";
        ArmHintText.Foreground = BrushFromHex(LiveCommandArmCheckBox.IsChecked == true ? "#8A3D2E" : "#64748B");
        UpdateCommandButtons();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
        => Close();

    private async Task ExecuteCommandAsync(
        Iec61850ControlValue? controlValue,
        Iec61850ControlStatusState? expectedState,
        string commandLabel)
    {
        if (_controlSession == null || controlValue == null || _isBusy)
            return;
        if (LiveCommandArmCheckBox.IsChecked != true)
        {
            MessageBox.Show(
                this,
                "Enable live command only after confirming that the IED and test circuit are in a safe condition.",
                "Live command locked",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Iec61850ControlRequest request;
        try
        {
            request = BuildRequest(controlValue);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "Invalid control parameters", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var descriptor = _controlSession.Descriptor;
        var current = _lastStatus?.DisplayValue ?? "Unknown";
        var testText = request.Test ? "\nMode: TEST=true (process movement is not expected)" : string.Empty;
        var confirmation =
            $"Control object: {descriptor.ObjectReference}\n" +
            $"Current status: {current}\n" +
            $"Command: {commandLabel}\n" +
            $"Model: {FriendlyControlModel(descriptor.ControlModel)}\n" +
            $"Interlock check: {YesNo(request.InterlockCheck)}\n" +
            $"Synchrocheck: {YesNo(request.SynchroCheck)}" +
            testText +
            "\n\nSend this command?";

        if (MessageBox.Show(this, confirmation, $"Confirm {commandLabel}", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _operationCts?.Dispose();
        var operationCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _operationCts = operationCts;
        SetBusy(true, $"Sending {commandLabel} command...");
        ControlValueText.Text = $"Control value: {commandLabel}";
        AddEvidence(
            "Sequence",
            "Started",
            $"{FriendlySequence(descriptor.ControlModel)}; Test={request.Test}; Check=sync:{request.SynchroCheck}/interlock:{request.InterlockCheck}");

        try
        {
            var result = await _controlSession.OperateAsync(request, operationCts.Token);
            ApplyActionResult(result, commandLabel);

            if (!result.IsSuccess)
            {
                await RefreshStatusCoreAsync(_lifetimeCts.Token);
                MessageBox.Show(
                    this,
                    BuildUserFailureMessage(result),
                    $"{commandLabel} not completed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (request.Test)
            {
                await RefreshStatusCoreAsync(_lifetimeCts.Token);
                FooterStatusText.Text = $"{commandLabel} test command accepted. Process feedback change is not required in Test mode.";
                AddEvidence("Feedback", "Test mode", $"Status after test: {_lastStatus?.DisplayValue ?? "Unknown"}");
                return;
            }

            FooterStatusText.Text = result.CommandTerminationReceived
                ? "Positive command termination received; confirming process status..."
                : "MMS control service accepted; confirming process status...";

            var feedbackConfirmed = await WaitForExpectedStatusAsync(expectedState, TimeSpan.FromSeconds(6), operationCts.Token);
            var confirmedStatus = _lastStatus;
            if (feedbackConfirmed && confirmedStatus != null)
            {
                FooterStatusText.Text = $"Command completed. Live status is {confirmedStatus.DisplayValue}.";
                AddEvidence("Feedback", "Confirmed", $"{confirmedStatus.Reference} = {confirmedStatus.DisplayValue}");
            }
            else
            {
                FooterStatusText.Text = result.CommandTerminationReceived && result.PositiveTermination
                    ? "CommandTermination was positive, but the status value did not reach the requested state within 6 seconds."
                    : "Command was accepted, but process feedback was not confirmed within 6 seconds.";
                AddEvidence("Feedback", "Not confirmed", $"Last status: {_lastStatus?.DisplayValue ?? "Unknown"}");
            }
        }
        catch (OperationCanceledException)
        {
            FooterStatusText.Text = "Control operation cancelled.";
            AddEvidence("Sequence", "Cancelled", "Client cancellation completed.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or TimeoutException or NotSupportedException)
        {
            FooterStatusText.Text = "Control operation failed before a safe completion boundary.";
            AddEvidence("Sequence", "Exception", ex.Message);
            TechnicalEvidenceTextBox.Text = ex.ToString();
            MessageBox.Show(this, ex.Message, "Control error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, FooterStatusText.Text);
            if (ReferenceEquals(_operationCts, operationCts))
                _operationCts = null;
            operationCts.Dispose();
        }
    }

    private Iec61850ControlRequest BuildRequest(Iec61850ControlValue controlValue)
    {
        var originIdentifier = OriginIdentifierTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(originIdentifier))
            throw new InvalidOperationException("Orig ID cannot be empty.");
        if (originIdentifier.Any(character => character > 0x7F))
            throw new InvalidOperationException("Orig ID must contain ASCII characters so the transmitted identifier is deterministic.");

        var maxLength = OriginIdentifierTextBox.MaxLength;
        if (Encoding.ASCII.GetByteCount(originIdentifier) > maxLength)
            throw new InvalidOperationException($"Orig ID exceeds the live limit of {maxLength} octets.");

        if (OriginatorCombo.SelectedItem is not OriginatorOption originator)
            throw new InvalidOperationException("Choose an Originator category.");

        return new Iec61850ControlRequest
        {
            ControlValue = controlValue,
            Origin = Iec61850Origin.FromText(originIdentifier, originator.Category),
            Test = TestCheckBox.IsChecked == true,
            InterlockCheck = InterlockCheckBox.IsChecked == true,
            SynchroCheck = SynchroCheckBox.IsChecked == true,
            AutoSelect = true,
            CommandTerminationTimeout = TimeSpan.FromSeconds(GetSelectedTimeoutSeconds())
        };
    }

    private void ApplyDescriptor(Iec61850ControlObjectDescriptor descriptor)
    {
        ControlObjectText.Text = descriptor.ObjectReference;
        StatusReferenceText.Text = string.IsNullOrWhiteSpace(descriptor.StatusReference)
            ? "Status reference: not exposed by live model"
            : string.IsNullOrWhiteSpace(descriptor.StatusFunctionalConstraint)
                ? $"Status reference: {descriptor.StatusReference}"
                : $"Status reference: {descriptor.StatusReference} [{descriptor.StatusFunctionalConstraint}]";
        ControlModelText.Text = FriendlyControlModel(descriptor.ControlModel);
        SequenceText.Text = FriendlySequence(descriptor.ControlModel);
        CompletionBoundaryText.Text = descriptor.IsEnhanced ? "CommandTermination" : "MMS accepted + feedback";
        ControlModelBadge.Background = BrushFromHex(descriptor.RequiresSelect ? "#E8E3F8" : "#DDEEF9");
        ControlModelText.Foreground = BrushFromHex(descriptor.RequiresSelect ? "#5B3FA3" : "#145E8C");

        var originLimit = FindNamedField(descriptor.OperSpecification, "orIdent")?.Size;
        if (originLimit is > 0 and <= 1024)
            OriginIdentifierTextBox.MaxLength = originLimit.Value;

        ConfigureSimpleCommandSurface(descriptor);
        TechnicalEvidenceTextBox.Text =
            $"Object: {descriptor.ObjectReference}\n" +
            $"CDC: {descriptor.Cdc}\n" +
            $"Control model: {descriptor.ControlModel}\n" +
            $"ctlVal type: {descriptor.CtlValSpecification.Signature}\n" +
            $"Oper type: {descriptor.OperSpecification.Signature}\n" +
            $"SBO timeout: {descriptor.SboTimeout}\n" +
            $"Operate timeout: {descriptor.OperTimeout}\n" +
            $"CommandTermination: {descriptor.SupportsCommandTermination}\n" +
            $"Discovery: {descriptor.DiscoveryEvidence}";
    }

    private void ConfigureSimpleCommandSurface(Iec61850ControlObjectDescriptor descriptor)
    {
        var cdc = descriptor.Cdc.Trim();
        var ctlType = descriptor.CtlValSpecification.MmsType.Trim();
        if (cdc.Equals("DPC", StringComparison.OrdinalIgnoreCase) ||
            (ctlType.Equals("bit-string", StringComparison.OrdinalIgnoreCase) && descriptor.CtlValSpecification.Size == 2))
        {
            _primaryLabel = "OPEN";
            _secondaryLabel = "CLOSE";
            _primaryValue = Iec61850ControlValue.Open();
            _secondaryValue = Iec61850ControlValue.Close();
            _primaryExpectedState = Iec61850ControlStatusState.Open;
            _secondaryExpectedState = Iec61850ControlStatusState.Closed;
            CommandHintText.Text = "Choose OPEN or CLOSE. Direct Operate / SBO is handled automatically.";
        }
        else if (cdc.Equals("SPC", StringComparison.OrdinalIgnoreCase) || ctlType.Equals("boolean", StringComparison.OrdinalIgnoreCase))
        {
            _primaryLabel = "OFF";
            _secondaryLabel = "ON";
            _primaryValue = Iec61850ControlValue.Off();
            _secondaryValue = Iec61850ControlValue.On();
            _primaryExpectedState = Iec61850ControlStatusState.Off;
            _secondaryExpectedState = Iec61850ControlStatusState.On;
            CommandHintText.Text = "Choose OFF or ON. Direct Operate / SBO is handled automatically.";
        }
        else if (cdc.Contains("INC", StringComparison.OrdinalIgnoreCase) || cdc.Contains("ISC", StringComparison.OrdinalIgnoreCase))
        {
            _primaryLabel = "LOWER";
            _secondaryLabel = "RAISE";
            _primaryValue = Iec61850ControlValue.Lower();
            _secondaryValue = Iec61850ControlValue.Raise();
            _primaryExpectedState = null;
            _secondaryExpectedState = null;
            CommandHintText.Text = "Choose LOWER or RAISE. Process feedback is displayed after service completion.";
        }
        else
        {
            _primaryValue = null;
            _secondaryValue = null;
            _primaryExpectedState = null;
            _secondaryExpectedState = null;
            CommandHintText.Text = $"Beginner command buttons do not map safely to CDC '{descriptor.Cdc}' / ctlVal '{ctlType}'.";
        }

        OpenButton.Content = _primaryLabel;
        CloseButton.Content = _secondaryLabel;
        UpdateCommandButtons();
    }

    private async Task RefreshStatusCoreAsync(CancellationToken cancellationToken)
    {
        if (_controlSession == null)
            return;

        var result = await _controlSession.ReadStatusAsync(cancellationToken);
        _lastStatus = result;
        StatusValueText.Text = result.DisplayValue;
        StatusReadTimeText.Text = result.IsSuccess
            ? $"Read {result.ReadAtUtc.ToLocalTime():HH:mm:ss.fff}"
            : "Status read failed";
        ApplyStatusAppearance(result.State);
        AddEvidence("Status", result.IsSuccess ? "Read" : "Failed", result.IsSuccess ? result.DisplayValue : result.Message);
        UpdateCommandButtons();
    }

    private async Task<bool> WaitForExpectedStatusAsync(
        Iec61850ControlStatusState? expectedState,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        do
        {
            await RefreshStatusCoreAsync(cancellationToken);
            if (expectedState == null)
                return _lastStatus?.IsSuccess == true;
            if (_lastStatus?.IsSuccess == true && _lastStatus.State == expectedState)
                return true;

            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return false;
    }

    private void ApplyActionResult(Iec61850ControlActionResult result, string commandLabel)
    {
        var resultLabel = result.IsSuccess ? "Accepted" : result.CompletionState.ToString();
        var details = result.IsSuccess
            ? result.CommandTerminationReceived
                ? result.PositiveTermination ? "Positive CommandTermination" : "Negative CommandTermination"
                : "MMS control service accepted"
            : BuildUserFailureMessage(result).Replace(Environment.NewLine, " ", StringComparison.Ordinal);
        AddEvidence(commandLabel, resultLabel, details);

        var diagnostics = result.Diagnostics.Count == 0
            ? "-"
            : string.Join(Environment.NewLine, result.Diagnostics);
        TechnicalEvidenceTextBox.Text =
            $"Action: {result.Action}\n" +
            $"Completion: {result.CompletionState}\n" +
            $"Request accepted: {result.RequestAccepted}\n" +
            $"CommandTermination received: {result.CommandTerminationReceived}\n" +
            $"Positive termination: {result.PositiveTermination}\n" +
            $"ctlNum: {result.ControlNumber?.ToString(CultureInfo.InvariantCulture) ?? "-"}\n" +
            $"T: {result.SequenceTimestamp?.ToString("O", CultureInfo.InvariantCulture) ?? "-"}\n" +
            $"Elapsed: {result.Elapsed.TotalMilliseconds:0.###} ms\n" +
            $"Client error: {result.ClientError}\n" +
            $"Control error: {result.ControlError}\n" +
            $"AddCause: {result.AddCause}\n" +
            $"LastApplError: {result.LastApplErrorText}\n" +
            $"Diagnostics: {diagnostics}\n\n" +
            $"Request hex:\n{result.RequestHex}\n\n" +
            $"Response / termination hex:\n{result.ResponseHex}";
    }

    private void ApplyStatusAppearance(Iec61850ControlStatusState state)
    {
        (string background, string foreground) = state switch
        {
            Iec61850ControlStatusState.Open or Iec61850ControlStatusState.Off => ("#DDF4EA", "#12644E"),
            Iec61850ControlStatusState.Closed or Iec61850ControlStatusState.On => ("#FBE5E0", "#943B29"),
            Iec61850ControlStatusState.Intermediate => ("#FFF1CC", "#8A5A00"),
            Iec61850ControlStatusState.Bad => ("#FDE2E7", "#A3243D"),
            _ => ("#E8EEF6", "#334155")
        };
        StatusBadge.Background = BrushFromHex(background);
        StatusValueText.Foreground = BrushFromHex(foreground);
    }

    private void SetBusy(bool busy, string status)
    {
        _isBusy = busy;
        FooterStatusText.Text = status;
        StopButton.IsEnabled = busy && _operationCts != null;
        OriginatorCombo.IsEnabled = !busy;
        OriginIdentifierTextBox.IsEnabled = !busy;
        TimeoutCombo.IsEnabled = !busy;
        InterlockCheckBox.IsEnabled = !busy;
        SynchroCheckBox.IsEnabled = !busy;
        TestCheckBox.IsEnabled = !busy;
        LiveCommandArmCheckBox.IsEnabled = !busy;
        UpdateCommandButtons();
    }

    private void UpdateCommandButtons()
    {
        var ready = !_isBusy &&
                    LiveCommandArmCheckBox.IsChecked == true &&
                    _controlSession?.Descriptor.IsOperationallyReady == true;
        var allowRepeatedTarget = TestCheckBox.IsChecked == true;
        var primaryIsCurrent = !allowRepeatedTarget &&
                               _primaryExpectedState != null &&
                               _lastStatus?.IsSuccess == true &&
                               _lastStatus.State == _primaryExpectedState;
        var secondaryIsCurrent = !allowRepeatedTarget &&
                                 _secondaryExpectedState != null &&
                                 _lastStatus?.IsSuccess == true &&
                                 _lastStatus.State == _secondaryExpectedState;

        OpenButton.IsEnabled = ready && _primaryValue != null && !primaryIsCurrent;
        CloseButton.IsEnabled = ready && _secondaryValue != null && !secondaryIsCurrent;
        OpenButton.ToolTip = primaryIsCurrent
            ? $"The live status is already {_primaryLabel}. Enable Test=true to test the command without requiring a state change."
            : $"Send {_primaryLabel} to the selected control object.";
        CloseButton.ToolTip = secondaryIsCurrent
            ? $"The live status is already {_secondaryLabel}. Enable Test=true to test the command without requiring a state change."
            : $"Send {_secondaryLabel} to the selected control object.";
    }

    private void AddEvidence(string step, string result, string details)
    {
        _evidence.Insert(0, new ControlEvidenceRow
        {
            Time = DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            Step = step,
            Result = result,
            Details = details
        });
        while (_evidence.Count > 100)
            _evidence.RemoveAt(_evidence.Count - 1);
    }

    private int GetSelectedTimeoutSeconds()
    {
        if (TimeoutCombo.SelectedItem is ComboBoxItem item &&
            int.TryParse(Convert.ToString(item.Tag, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return Math.Clamp(seconds, 1, 120);
        }
        return 10;
    }

    private static MmsTypeSpecificationNode? FindNamedField(MmsTypeSpecificationNode node, string requestedName)
    {
        if (NormalizeName(node.Name).Equals(NormalizeName(requestedName), StringComparison.Ordinal))
            return node;

        foreach (var child in node.Children)
        {
            var match = FindNamedField(child, requestedName);
            if (match != null)
                return match;
        }
        return null;
    }

    private static string BuildUserFailureMessage(Iec61850ControlActionResult result)
    {
        var lines = new List<string>
        {
            $"Completion: {result.CompletionState}"
        };
        if (!string.IsNullOrWhiteSpace(result.ClientError))
            lines.Add($"Client: {result.ClientError}");
        if (!string.IsNullOrWhiteSpace(result.ControlError))
            lines.Add($"Control error: {result.ControlError}");
        if (!string.IsNullOrWhiteSpace(result.AddCause))
            lines.Add($"AddCause: {result.AddCause}");
        if (!string.IsNullOrWhiteSpace(result.LastApplErrorText))
            lines.Add($"IED detail: {result.LastApplErrorText}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string FriendlyControlModel(Iec61850ControlModel model)
        => model switch
        {
            Iec61850ControlModel.DirectNormal => "Direct Operate (DO) • Normal",
            Iec61850ControlModel.DirectEnhanced => "Direct Operate (DO) • Enhanced",
            Iec61850ControlModel.SelectBeforeOperateNormal => "Select Before Operate (SBO) • Normal",
            Iec61850ControlModel.SelectBeforeOperateEnhanced => "Select Before Operate (SBO) • Enhanced",
            Iec61850ControlModel.StatusOnly => "Status only",
            _ => "Unknown"
        };

    private static string FriendlySequence(Iec61850ControlModel model)
        => model switch
        {
            Iec61850ControlModel.DirectNormal => "Operate → process feedback",
            Iec61850ControlModel.DirectEnhanced => "Operate → CommandTermination → feedback",
            Iec61850ControlModel.SelectBeforeOperateNormal => "SBO select → Operate → feedback",
            Iec61850ControlModel.SelectBeforeOperateEnhanced => "SBOw → Operate → CommandTermination → feedback",
            _ => "No safe command sequence available"
        };

    private static IReadOnlyList<OriginatorOption> BuildOriginatorOptions()
        => new[]
        {
            new OriginatorOption("Bay control", Iec61850OriginCategory.BayControl),
            new OriginatorOption("Station control", Iec61850OriginCategory.StationControl),
            new OriginatorOption("Remote control", Iec61850OriginCategory.RemoteControl),
            new OriginatorOption("Automatic bay", Iec61850OriginCategory.AutomaticBay),
            new OriginatorOption("Automatic station", Iec61850OriginCategory.AutomaticStation),
            new OriginatorOption("Automatic remote", Iec61850OriginCategory.AutomaticRemote),
            new OriginatorOption("Maintenance", Iec61850OriginCategory.Maintenance),
            new OriginatorOption("Process", Iec61850OriginCategory.Process)
        };

    private static string NormalizeName(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static Brush BrushFromHex(string color)
        => (Brush)new BrushConverter().ConvertFromString(color)!;

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private sealed record OriginatorOption(string DisplayName, Iec61850OriginCategory Category);
}

public sealed class ControlEvidenceRow
{
    public string Time { get; init; } = string.Empty;
    public string Step { get; init; } = string.Empty;
    public string Result { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
}
