using System.Collections.ObjectModel;
using System.Windows;
using AR.Iec61850.Binding;
using AR.Iec61850.Mms;

namespace AR.Iec61850.IedDiscovery.ViewModels;

public sealed class DataAttributeDetailRow : ObservableObject
{
    private string _value = "-";
    private string _quality = "-";
    private string _timestamp = "-";
    private string _status = "not read";
    private bool _isExpanded;

    public DataAttributeDetailRow(string name, string fc, string type, string reference, string source, int level = 0, Iec61850ValueSchemaNode? schema = null)
    {
        Name = name;
        Fc = fc;
        Type = type;
        Reference = reference;
        Source = source;
        Level = Math.Max(0, level);
        Schema = schema;
    }

    public string Name { get; }
    public string Fc { get; }
    public string Type { get; }
    public string Reference { get; }
    public string Source { get; }
    public int Level { get; }
    public Iec61850ValueSchemaNode? Schema { get; }
    public ObservableCollection<DataAttributeDetailRow> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;
    public bool IsExpanded { get => _isExpanded; set { if (SetProperty(ref _isExpanded, value)) { OnPropertyChanged(nameof(ExpanderGlyph)); } } }
    public string ExpanderGlyph => HasChildren ? (IsExpanded ? "▾" : "▸") : string.Empty;
    public Visibility ExpanderVisibility => HasChildren ? Visibility.Visible : Visibility.Hidden;
    public Thickness NameMargin => new(Level * 16, 0, 0, 0);
    public FontWeight NameWeight => HasChildren ? FontWeights.SemiBold : FontWeights.Normal;
    public string Value { get => _value; set => SetProperty(ref _value, value); }
    public string Quality { get => _quality; set => SetProperty(ref _quality, value); }
    public string Timestamp { get => _timestamp; set => SetProperty(ref _timestamp, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }

    public void ReplaceChildren(IEnumerable<DataAttributeDetailRow> rows, bool expand = true)
    {
        Children.Clear();
        foreach (var row in rows)
            Children.Add(row);

        IsExpanded = expand && Children.Count > 0;
        OnPropertyChanged(nameof(HasChildren));
        OnPropertyChanged(nameof(ExpanderGlyph));
        OnPropertyChanged(nameof(ExpanderVisibility));
        OnPropertyChanged(nameof(NameWeight));
    }
}

public static class DetailRowFlattener
{
    public static IReadOnlyList<DataAttributeDetailRow> Flatten(IEnumerable<DataAttributeDetailRow> roots)
    {
        var rows = new List<DataAttributeDetailRow>();
        foreach (var root in roots)
            Add(root, rows);
        return rows;
    }

    private static void Add(DataAttributeDetailRow row, ICollection<DataAttributeDetailRow> rows)
    {
        rows.Add(row);
        if (!row.IsExpanded)
            return;

        foreach (var child in row.Children)
            Add(child, rows);
    }
}

public static class MmsValueDetailTreeBuilder
{
    public static DataAttributeDetailRow FromBoundRow(Iec61850BoundValueRow row, int level = 0)
    {
        var detail = new DataAttributeDetailRow(row.Name, row.FunctionalConstraint, row.Type, row.Reference, row.SemanticKind, level)
        {
            Value = row.Value,
            Quality = row.Quality,
            Timestamp = row.Timestamp,
            Status = row.Status
        };
        detail.ReplaceChildren(row.Children.Select(child => FromBoundRow(child, level + 1)), expand: row.Children.Count > 0);
        return detail;
    }

    public static DataAttributeDetailRow FromSchema(Iec61850ValueSchemaNode schema, int level = 0, bool expandRoot = true)
    {
        var bound = Iec61850ValueBindingEngine.ToUnboundRow(schema);
        var detail = new DataAttributeDetailRow(bound.Name, bound.FunctionalConstraint, bound.Type, bound.Reference, schema.Source, level, schema)
        {
            Value = bound.Value,
            Quality = bound.Quality,
            Timestamp = bound.Timestamp,
            Status = bound.Status
        };
        detail.ReplaceChildren(schema.Children.Select(child => FromSchema(child, level + 1, expandRoot: true)), expand: schema.Children.Count > 0 && (level == 0 ? expandRoot : false));
        ApplySmartSummary(detail);
        return detail;
    }

    public static void ApplyReadValue(DataAttributeDetailRow target, MmsDataValue? value, string? reference = null)
    {
        if (target.Schema != null)
        {
            var result = Iec61850ValueBindingEngine.Bind(target.Schema, value);
            ApplyBoundValue(target, result.Root);
            foreach (var diagnostic in result.Diagnostics.Take(3))
                target.Status = diagnostic.StartsWith("TYPE_BINDING_MISMATCH", StringComparison.OrdinalIgnoreCase) ? "binding mismatch" : target.Status;
            return;
        }

        ApplyRawReadValue(target, value, reference);
    }

    private static void ApplyBoundValue(DataAttributeDetailRow target, Iec61850BoundValueRow bound)
    {
        target.Value = bound.Value;
        target.Quality = bound.Quality;
        target.Timestamp = bound.Timestamp == "-" ? DateTimeOffset.Now.ToLocalTime().ToString("HH:mm:ss") : bound.Timestamp;
        target.Status = bound.Status;
        var children = bound.Children.Select(child => FromBoundRow(child, target.Level + 1)).ToArray();
        target.ReplaceChildren(children, expand: children.Length > 0);
        ApplySmartSummary(target);
    }

    private static void ApplyRawReadValue(DataAttributeDetailRow target, MmsDataValue? value, string? reference = null)
    {
        var effectiveReference = string.IsNullOrWhiteSpace(reference) ? target.Reference : reference;
        target.Value = value == null ? "-" : MmsDataValueRenderer.ToCompactString(value, effectiveReference);
        target.Status = "read-raw";
        target.Timestamp = DateTimeOffset.Now.ToLocalTime().ToString("HH:mm:ss");

        var children = BuildRawChildren(value, effectiveReference, target.Fc, target.Level + 1).ToArray();
        target.ReplaceChildren(children, expand: children.Length > 0);
        ApplySmartSummary(target);
    }


    public static void ApplySmartSummaries(IEnumerable<DataAttributeDetailRow> roots)
    {
        foreach (var root in roots)
            ApplySmartSummary(root);
    }

    public static void ApplySmartSummary(DataAttributeDetailRow row)
    {
        foreach (var child in row.Children)
            ApplySmartSummary(child);

        if (!row.HasChildren)
            return;

        var summary = Iec61850SmartValueSummaryEngine.Summarize(ToPresentationNode(row));
        if (!string.IsNullOrWhiteSpace(summary.Value) && summary.Value != "-" && !summary.Value.StartsWith("Struct(", StringComparison.OrdinalIgnoreCase))
            row.Value = summary.Value;
        if (!string.IsNullOrWhiteSpace(summary.Marker))
            row.Status = summary.Marker;
    }

    private static Iec61850PresentationValueNode ToPresentationNode(DataAttributeDetailRow row)
        => new(
            row.Name,
            row.Fc,
            row.Type,
            row.Value,
            row.Status,
            row.Children.Select(ToPresentationNode).ToArray());

    private static IEnumerable<DataAttributeDetailRow> BuildRawChildren(MmsDataValue? value, string? reference, string fc, int level)
    {
        if (value == null || value.Kind is not (MmsDataKind.Structure or MmsDataKind.Array))
            yield break;

        for (var index = 0; index < value.Children.Count; index++)
        {
            var childValue = value.Children[index];
            var childName = $"[{index}]";
            var childReference = CombineReference(reference, childName);
            var child = new DataAttributeDetailRow(childName, fc, childValue.Kind.ToString(), childReference, "raw-positional", level)
            {
                Value = childValue.Kind is MmsDataKind.Structure or MmsDataKind.Array
                    ? $"{childValue.Kind}({childValue.Children.Count})"
                    : MmsDataValueRenderer.ToCompactString(childValue, childReference),
                Status = "raw"
            };
            child.ReplaceChildren(BuildRawChildren(childValue, childReference, fc, level + 1), expand: false);
            yield return child;
        }
    }

    private static string CombineReference(string? reference, string child)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return child;
        if (child.StartsWith("[", StringComparison.Ordinal))
            return reference + child;
        return reference + "." + child;
    }
}
