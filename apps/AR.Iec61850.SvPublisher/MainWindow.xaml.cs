using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private void RampMode_Click(object sender, RoutedEventArgs e)
        => _viewModel.Mode = InjectionMode.Ramp;

    private void StateSequencerMode_Click(object sender, RoutedEventArgs e)
        => _viewModel.Mode = InjectionMode.Sequencer;

    private void SequenceStateCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SequenceStateViewModel state })
            _viewModel.SelectedSequenceState = state;
    }

    private void ManualNumericTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            _ = CommitManualTextBox(textBox);
    }

    private void ManualNumericTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (e.Key is not (Key.Enter or Key.Up or Key.Down or Key.Left or Key.Right))
            return;

        var grid = FindVisualParent<DataGrid>(textBox) ?? ManualOutputsGrid;
        e.Handled = true;
        if (!CommitManualTextBox(textBox))
            return;

        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);
        MoveManualCell(grid, e.Key);
    }

    private void ManualOutputsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var grid = sender as DataGrid ?? ManualOutputsGrid;
        if (FindVisualParent<TextBox>(e.OriginalSource as DependencyObject) is not null)
            return;

        var cell = FindVisualParent<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell is null || cell.IsEditing || cell.Column.IsReadOnly)
            return;

        if (!CommitFocusedManualTextBox())
        {
            e.Handled = true;
            return;
        }

        FocusManualCell(grid, cell);
        if (IsManualNumericColumn(cell.Column))
        {
            e.Handled = true;
            grid.BeginEdit();
            FocusEditingTextBox(grid, selectAll: true);
        }
    }

    private void ManualOutputsGrid_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var grid = sender as DataGrid ?? ManualOutputsGrid;
        if (e.OriginalSource is TextBox)
            return;

        if (string.IsNullOrEmpty(e.Text) || !IsManualTypingText(e.Text))
            return;

        var column = grid.CurrentColumn;
        if (column is null || !IsManualNumericColumn(column))
            return;

        if (!CommitFocusedManualTextBox())
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        grid.BeginEdit();
        FocusEditingTextBox(grid, selectAll: false, seedText: e.Text);
    }

    private bool CommitManualTextBox(TextBox textBox)
    {
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        if (textBox.DataContext is not ManualOutputRowViewModel row || textBox.Tag is not string propertyName)
            return true;

        if (_viewModel.CommitManualRowText(row, propertyName, out var warning))
            return true;

        MessageBox.Show(this, warning, "Invalid analog output value", MessageBoxButton.OK, MessageBoxImage.Warning);
        textBox.Dispatcher.BeginInvoke(new Action(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        }));
        return false;
    }

    private bool CommitFocusedManualTextBox()
    {
        if (Keyboard.FocusedElement is TextBox focused && IsManualNumericTextBox(focused))
        {
            var grid = FindVisualParent<DataGrid>(focused) ?? ManualOutputsGrid;
            if (!CommitManualTextBox(focused))
                return false;

            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        return true;
    }

    private void MoveManualCell(DataGrid grid, Key key)
    {
        if (grid.Items.Count == 0)
            return;

        var rowIndex = grid.Items.IndexOf(grid.CurrentItem);
        if (rowIndex < 0)
            rowIndex = 0;

        var displayIndex = grid.CurrentColumn?.DisplayIndex ?? 0;
        switch (key)
        {
            case Key.Enter:
            case Key.Down:
                rowIndex++;
                break;
            case Key.Up:
                rowIndex--;
                break;
            case Key.Right:
                displayIndex++;
                break;
            case Key.Left:
                displayIndex--;
                break;
        }

        rowIndex = Math.Clamp(rowIndex, 0, grid.Items.Count - 1);
        displayIndex = Math.Clamp(displayIndex, 0, grid.Columns.Count - 1);

        var item = grid.Items[rowIndex];
        var column = grid.Columns.FirstOrDefault(c => c.DisplayIndex == displayIndex) ?? grid.Columns[displayIndex];
        grid.CurrentCell = new DataGridCellInfo(item, column);
        grid.SelectedItem = item;
        grid.ScrollIntoView(item, column);
        grid.Focus();
        if (IsManualNumericColumn(column))
        {
            grid.BeginEdit();
            FocusEditingTextBox(grid, selectAll: true);
        }
    }

    private void ManualOutputsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var grid = sender as DataGrid ?? ManualOutputsGrid;
        if (!CommitFocusedManualTextBox())
        {
            e.Handled = true;
            return;
        }

        var cell = FindVisualParent<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell is null)
        {
            _viewModel.SetManualContext(null, string.Empty);
            return;
        }

        FocusManualCell(grid, cell);
        grid.CommitEdit(DataGridEditingUnit.Cell, true);
        grid.CommitEdit(DataGridEditingUnit.Row, true);

        if (cell.DataContext is ManualOutputRowViewModel row)
            _viewModel.SetManualContext(row, cell.Column.Header?.ToString() ?? string.Empty);
    }

    private static void FocusManualCell(DataGrid grid, DataGridCell cell)
    {
        cell.Focus();
        if (cell.DataContext is ManualOutputRowViewModel row)
        {
            grid.CurrentCell = new DataGridCellInfo(row, cell.Column);
            grid.SelectedItem = row;
        }
    }

    private void FocusEditingTextBox(DataGrid grid, bool selectAll, string? seedText = null)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (FindVisualChild<TextBox>(grid) is not { } textBox)
                return;

            textBox.Focus();
            if (seedText is not null)
            {
                textBox.Text = seedText;
                textBox.CaretIndex = textBox.Text.Length;
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            }
            else if (selectAll)
            {
                textBox.SelectAll();
            }
        }));
    }

    private static bool IsManualNumericColumn(DataGridColumn column)
    {
        var header = column.Header?.ToString();
        return string.Equals(header, "Value", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(header, "Angle", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(header, "Freq", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsManualNumericTextBox(TextBox textBox)
        => textBox.Tag is "Magnitude" or "AngleDegrees" or "FrequencyHz";

    private static bool IsManualTypingText(string text)
        => text.All(ch => char.IsDigit(ch) || ch is '-' or '+' or '.' or ',');

    private void RampStateTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            _ = CommitRampTextBox(textBox);
    }

    private void RampStateTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (e.Key is not (Key.Enter or Key.Up or Key.Down or Key.Left or Key.Right))
            return;

        e.Handled = true;
        if (!CommitRampTextBox(textBox))
            return;

        RampStatesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RampStatesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        MoveRampCell(e.Key);
    }

    private void RampStatesGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<TextBox>(e.OriginalSource as DependencyObject) is not null)
            return;

        var cell = FindVisualParent<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell is null || cell.IsEditing || cell.Column.IsReadOnly)
            return;

        if (!CommitFocusedRampTextBox())
        {
            e.Handled = true;
            return;
        }

        FocusRampCell(cell);
        e.Handled = true;
        RampStatesGrid.BeginEdit();
        FocusRampEditingTextBox(selectAll: true);
    }

    private void RampStatesGrid_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.OriginalSource is TextBox)
            return;

        if (string.IsNullOrEmpty(e.Text) || !IsRampTypingText(e.Text))
            return;

        var column = RampStatesGrid.CurrentColumn;
        if (column is null || !IsRampNumericColumn(column))
            return;

        if (!CommitFocusedRampTextBox())
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        RampStatesGrid.BeginEdit();
        FocusRampEditingTextBox(selectAll: false, seedText: e.Text);
    }

    private void RampStatesGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!CommitFocusedRampTextBox())
        {
            e.Handled = true;
            return;
        }

        var cell = FindVisualParent<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell is null)
            return;

        FocusRampCell(cell);
        RampStatesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RampStatesGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }

    private bool CommitRampTextBox(TextBox textBox)
    {
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        if (textBox.DataContext is not RampStateViewModel row || textBox.Tag is not string propertyName)
            return true;

        if (row.CommitText(propertyName, out var warning))
            return true;

        MessageBox.Show(this, warning, "Invalid ramp value", MessageBoxButton.OK, MessageBoxImage.Warning);
        textBox.Dispatcher.BeginInvoke(new Action(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        }));
        return false;
    }

    private bool CommitFocusedRampTextBox()
    {
        if (Keyboard.FocusedElement is TextBox focused && IsRampNumericTextBox(focused))
        {
            if (!CommitRampTextBox(focused))
                return false;

            RampStatesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            RampStatesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        return true;
    }

    private void MoveRampCell(Key key)
    {
        if (RampStatesGrid.Items.Count == 0)
            return;

        var rowIndex = RampStatesGrid.Items.IndexOf(RampStatesGrid.CurrentItem);
        if (rowIndex < 0)
            rowIndex = 0;

        var displayIndex = RampStatesGrid.CurrentColumn?.DisplayIndex ?? 0;
        switch (key)
        {
            case Key.Enter:
            case Key.Down:
                rowIndex++;
                break;
            case Key.Up:
                rowIndex--;
                break;
            case Key.Right:
                displayIndex++;
                break;
            case Key.Left:
                displayIndex--;
                break;
        }

        rowIndex = Math.Clamp(rowIndex, 0, RampStatesGrid.Items.Count - 1);
        displayIndex = Math.Clamp(displayIndex, 0, RampStatesGrid.Columns.Count - 1);

        var item = RampStatesGrid.Items[rowIndex];
        var column = RampStatesGrid.Columns.FirstOrDefault(c => c.DisplayIndex == displayIndex) ?? RampStatesGrid.Columns[displayIndex];
        RampStatesGrid.CurrentCell = new DataGridCellInfo(item, column);
        RampStatesGrid.SelectedItem = item;
        RampStatesGrid.ScrollIntoView(item, column);
        RampStatesGrid.Focus();
        if (!column.IsReadOnly)
        {
            RampStatesGrid.BeginEdit();
            FocusRampEditingTextBox(selectAll: true);
        }
    }

    private void FocusRampCell(DataGridCell cell)
    {
        cell.Focus();
        if (cell.DataContext is RampStateViewModel row)
        {
            RampStatesGrid.CurrentCell = new DataGridCellInfo(row, cell.Column);
            RampStatesGrid.SelectedItem = row;
        }
    }

    private void FocusRampEditingTextBox(bool selectAll, string? seedText = null)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (FindVisualChild<TextBox>(RampStatesGrid) is not { } textBox)
                return;

            textBox.Focus();
            if (seedText is not null)
            {
                textBox.Text = seedText;
                textBox.CaretIndex = textBox.Text.Length;
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            }
            else if (selectAll)
            {
                textBox.SelectAll();
            }
        }));
    }

    private static bool IsRampNumericColumn(DataGridColumn column)
    {
        var header = column.Header?.ToString();
        return string.Equals(header, "From", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(header, "To", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(header, "Step", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(header, "dt", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(header, "Steps", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(header, "Time", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRampNumericTextBox(TextBox textBox)
        => textBox.Tag is "From" or "To" or "Step" or "StepTimeSeconds" or "Steps" or "TimeSeconds";

    private static bool IsRampTypingText(string text)
        => text.All(ch => char.IsDigit(ch) || ch is '-' or '+' or '.' or ',');

    private static T? FindVisualParent<T>(DependencyObject? child)
        where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T typed)
                return typed;

            try
            {
                child = VisualTreeHelper.GetParent(child);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                return typed;

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
                return descendant;
        }

        return null;
    }
}
