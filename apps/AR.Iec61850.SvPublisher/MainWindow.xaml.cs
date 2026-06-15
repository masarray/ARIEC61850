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

        e.Handled = true;
        if (!CommitManualTextBox(textBox))
            return;

        ManualOutputsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ManualOutputsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        MoveManualCell(e.Key);
    }

    private void ManualOutputsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
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

        FocusManualCell(cell);
        if (IsManualNumericColumn(cell.Column))
        {
            e.Handled = true;
            ManualOutputsGrid.BeginEdit();
            FocusEditingTextBox(selectAll: true);
        }
    }

    private void ManualOutputsGrid_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.OriginalSource is TextBox)
            return;

        if (string.IsNullOrEmpty(e.Text) || !IsManualTypingText(e.Text))
            return;

        var column = ManualOutputsGrid.CurrentColumn;
        if (column is null || !IsManualNumericColumn(column))
            return;

        if (!CommitFocusedManualTextBox())
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        ManualOutputsGrid.BeginEdit();
        FocusEditingTextBox(selectAll: false, seedText: e.Text);
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
            if (!CommitManualTextBox(focused))
                return false;

            ManualOutputsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ManualOutputsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        return true;
    }

    private void MoveManualCell(Key key)
    {
        if (ManualOutputsGrid.Items.Count == 0)
            return;

        var rowIndex = ManualOutputsGrid.Items.IndexOf(ManualOutputsGrid.CurrentItem);
        if (rowIndex < 0)
            rowIndex = 0;

        var displayIndex = ManualOutputsGrid.CurrentColumn?.DisplayIndex ?? 0;
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

        rowIndex = Math.Clamp(rowIndex, 0, ManualOutputsGrid.Items.Count - 1);
        displayIndex = Math.Clamp(displayIndex, 0, ManualOutputsGrid.Columns.Count - 1);

        var item = ManualOutputsGrid.Items[rowIndex];
        var column = ManualOutputsGrid.Columns.FirstOrDefault(c => c.DisplayIndex == displayIndex) ?? ManualOutputsGrid.Columns[displayIndex];
        ManualOutputsGrid.CurrentCell = new DataGridCellInfo(item, column);
        ManualOutputsGrid.SelectedItem = item;
        ManualOutputsGrid.ScrollIntoView(item, column);
        ManualOutputsGrid.Focus();
        if (IsManualNumericColumn(column))
        {
            ManualOutputsGrid.BeginEdit();
            FocusEditingTextBox(selectAll: true);
        }
    }

    private void ManualOutputsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
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

        FocusManualCell(cell);
        ManualOutputsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ManualOutputsGrid.CommitEdit(DataGridEditingUnit.Row, true);

        if (cell.DataContext is ManualOutputRowViewModel row)
            _viewModel.SetManualContext(row, cell.Column.Header?.ToString() ?? string.Empty);
    }

    private void FocusManualCell(DataGridCell cell)
    {
        cell.Focus();
        if (cell.DataContext is ManualOutputRowViewModel row)
        {
            ManualOutputsGrid.CurrentCell = new DataGridCellInfo(row, cell.Column);
            ManualOutputsGrid.SelectedItem = row;
        }
    }

    private void FocusEditingTextBox(bool selectAll, string? seedText = null)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (FindVisualChild<TextBox>(ManualOutputsGrid) is not { } textBox)
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
