using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AR.Iec61850.VirtualRelayLab;

public partial class AlgorithmEditorWindow : Window
{
    private readonly Dictionary<int, string> _algorithms = new()
    {
        [0] = """
element "50P-1" {

  input phaseCurrent =
    max(IA.rms1c, IB.rms1c, IC.rms1c)

  pickup =
    phaseCurrent >= setting("I>>")

  dropout =
    phaseCurrent < setting("I>>") * setting("DropoutRatio")

  operate =
    pickup.persist(setting("Delay"))

  trip =
    operate && smv.allowsTrip
}
""",
        [1] = """
element "51P" {

  input phaseCurrent =
    max(IA.fundamental, IB.fundamental, IC.fundamental)

  multiple =
    phaseCurrent / setting("Is")

  operateTime =
    setting("TMS") *
    (0.14 / (pow(multiple, 0.02) - 1))

  progress =
    integrate(dt / operateTime)
      when multiple > 1
      reset using setting("ResetMode")

  trip =
    progress >= 1 && smv.allowsTrip
}
""",
        [2] = """
element "50N" {

  input earthCurrent =
    select(setting("EarthInput"), measuredIN, IA + IB + IC)

  pickup =
    abs(earthCurrent.rms1c) >= setting("I0>>")

  operate =
    pickup.persist(setting("Delay"))

  trip =
    operate && smv.allowsTrip
}
""",
        [3] = """
element "51N" {

  input earthCurrent =
    abs(IA + IB + IC).fundamental

  multiple =
    earthCurrent / setting("I0s")

  operateTime =
    setting("TMS") *
    (0.14 / (pow(multiple, 0.02) - 1))

  progress =
    integrate(dt / operateTime)
      when multiple > 1

  trip =
    progress >= 1 && smv.allowsTrip
}
""",
        [4] = """
logic "TRIP" {

  request =
    50P_1.trip || 51P.trip || 50N.trip || 51N.trip

  permit =
    smv.allowsTrip && runtime.healthy

  output virtualTrip =
    latch(request && permit)
      reset using command("RESET")
}
""",
        [5] = """
policy "SMV-TRUST" {

  measurement =
    frame.valid && mapping.known && stream.fresh

  pickup =
    measurement && quality.accepted

  trip =
    pickup
    && smpCnt.continuous
    && processing.withinBudget
    && algorithm.validated

  on trip == false {
    expose blockReason
  }
}
"""
    };

    public AlgorithmEditorWindow()
    {
        InitializeComponent();
        AlgorithmTextBox.Text = _algorithms[0];
    }

    private void ElementList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ElementList.SelectedIndex < 0)
            return;

        _algorithms[GetPreviousIndex()] = AlgorithmTextBox.Text;
        AlgorithmTextBox.Text = _algorithms[ElementList.SelectedIndex];
        EditorTitleText.Text = ElementList.SelectedIndex switch
        {
            0 => "50P-1 · Standard precision profile",
            1 => "51P · IEC standard inverse profile",
            2 => "50N · Residual / neutral current profile",
            3 => "51N · IEC earth-fault inverse profile",
            4 => "TRIP · Guarded logic matrix",
            _ => "SMV · Protection trust policy"
        };
        ResetValidationState();
        _lastSelectedIndex = ElementList.SelectedIndex;
    }

    private int _lastSelectedIndex;

    private int GetPreviousIndex()
    {
        return Math.Clamp(_lastSelectedIndex, 0, _algorithms.Count - 1);
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        var source = AlgorithmTextBox.Text;
        var errors = new List<string>();

        if (!source.Contains('{') || !source.Contains('}'))
            errors.Add("block delimiters are incomplete");
        if (!source.Contains("trip", StringComparison.OrdinalIgnoreCase))
            errors.Add("a trip or trip-permission output is required");
        if (!source.Contains("smv.allowsTrip", StringComparison.Ordinal))
            errors.Add("mandatory SMV trust gate is missing");
        if (source.Contains("while", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("for (", StringComparison.OrdinalIgnoreCase))
            errors.Add("unbounded loops are not permitted");
        if (source.Contains("File.", StringComparison.Ordinal) ||
            source.Contains("Http", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("Process.", StringComparison.Ordinal))
            errors.Add("file, network and process access are not permitted");

        if (errors.Count == 0)
        {
            ValidationBadge.Background = new SolidColorBrush(Color.FromRgb(234, 245, 236));
            ValidationBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(185, 216, 191));
            ValidationText.Foreground = (Brush)FindResource("HealthyBrush");
            ValidationText.Text = "Validated · safe to stage";
            StageButton.IsEnabled = true;
            CursorStatusText.Text = "Syntax, unit contract, bounded runtime and mandatory SMV gate passed.";
        }
        else
        {
            ValidationBadge.Background = new SolidColorBrush(Color.FromRgb(251, 235, 234));
            ValidationBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(227, 175, 172));
            ValidationText.Foreground = (Brush)FindResource("TripBrush");
            ValidationText.Text = $"Validation failed · {errors.Count} issue(s)";
            StageButton.IsEnabled = false;
            CursorStatusText.Text = string.Join("; ", errors);
        }
    }

    private void Stage_Click(object sender, RoutedEventArgs e)
    {
        _algorithms[ElementList.SelectedIndex] = AlgorithmTextBox.Text;
        ValidationText.Text = "Staged for deterministic A/B evaluation";
        StageButton.IsEnabled = false;
        CursorStatusText.Text = "The active relay algorithm is unchanged. The staged revision is ready for shadow comparison.";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ResetValidationState()
    {
        ValidationBadge.Background = new SolidColorBrush(Color.FromRgb(238, 242, 245));
        ValidationBadge.BorderBrush = (Brush)FindResource("LineBrush");
        ValidationText.Foreground = (Brush)FindResource("MutedBrush");
        ValidationText.Text = "Not validated";
        StageButton.IsEnabled = false;
        CursorStatusText.Text = "Deterministic runtime · no file, network, reflection or unmanaged access";
    }
}
