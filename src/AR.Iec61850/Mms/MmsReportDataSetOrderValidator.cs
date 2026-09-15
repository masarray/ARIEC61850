namespace AR.Iec61850.Mms;

public sealed class MmsReportDataSetOrderValidationResult
{
    public bool IsValid { get; init; }
    public int ExpectedMemberCount { get; init; }
    public int IncludedMemberCount { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();

    public string Summary => IsValid
        ? $"report DataSet order valid: included={IncludedMemberCount}, expectedDirectoryMembers={ExpectedMemberCount}."
        : $"report DataSet order invalid: included={IncludedMemberCount}, expectedDirectoryMembers={ExpectedMemberCount}, diagnostics={Diagnostics.Count}.";
}

/// <summary>
/// Verifies that the already-decoded report projection still agrees with the exact
/// DataSet directory order used by the subscription plan. This is deliberately a
/// second boundary check: a report with ambiguous/out-of-range member identity is not
/// silently accepted merely because BER decoding succeeded.
/// </summary>
public static class MmsReportDataSetOrderValidator
{
    public static MmsReportDataSetOrderValidationResult Validate(
        MmsReportFrame report,
        IReadOnlyList<MmsDataSetDirectoryMember> expectedMembers)
    {
        ArgumentNullException.ThrowIfNull(report);
        expectedMembers ??= Array.Empty<MmsDataSetDirectoryMember>();

        var diagnostics = new List<string>();
        var included = report.IncludedDataSetIndexes ?? Array.Empty<int>();

        var previous = -1;
        for (var position = 0; position < included.Count; position++)
        {
            var index = included[position];
            if (index < 0 || index >= expectedMembers.Count)
            {
                diagnostics.Add($"Included DataSet index {index} at report position {position} is outside directory range 0..{Math.Max(0, expectedMembers.Count - 1)}.");
                continue;
            }

            if (index <= previous)
                diagnostics.Add($"Included DataSet indexes are not strictly increasing at report position {position}: previous={previous}, current={index}.");
            previous = index;
        }

        if (report.Values.Count != included.Count)
        {
            diagnostics.Add($"Mapped report value count {report.Values.Count} does not match inclusion count {included.Count}.");
        }

        var comparable = Math.Min(report.Values.Count, included.Count);
        for (var position = 0; position < comparable; position++)
        {
            var expectedIndex = included[position];
            var value = report.Values[position];
            if (value.Index != expectedIndex)
            {
                diagnostics.Add($"Mapped report value at position {position} carries DataSet index {value.Index}; inclusion order requires {expectedIndex}.");
                continue;
            }

            if (expectedIndex < 0 || expectedIndex >= expectedMembers.Count)
                continue;

            var expected = expectedMembers[expectedIndex];
            var expectedReference = Normalize(expected.UserReference);
            var mappedReference = Normalize(value.Member?.UserReference);
            if (!string.IsNullOrWhiteSpace(expectedReference) &&
                !string.IsNullOrWhiteSpace(mappedReference) &&
                !string.Equals(expectedReference, mappedReference, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add($"DataSet member identity mismatch at index {expectedIndex}: decoded member does not match the subscription directory order.");
            }

            if (value.Member is not null &&
                !string.IsNullOrWhiteSpace(expected.FunctionalConstraint) &&
                !string.IsNullOrWhiteSpace(value.Member.FunctionalConstraint) &&
                !string.Equals(expected.FunctionalConstraint, value.Member.FunctionalConstraint, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add($"Functional-constraint mismatch at DataSet index {expectedIndex}: expected {expected.FunctionalConstraint}, decoded {value.Member.FunctionalConstraint}.");
            }
        }

        if (report.DecoderMode.Equals("rejected-unmapped", StringComparison.OrdinalIgnoreCase))
            diagnostics.Add("Report mapper already rejected the frame as unmapped; DataSet-order validation cannot promote it to a process-value report.");

        return new MmsReportDataSetOrderValidationResult
        {
            IsValid = diagnostics.Count == 0,
            ExpectedMemberCount = expectedMembers.Count,
            IncludedMemberCount = included.Count,
            Diagnostics = diagnostics
        };
    }

    public static IReadOnlyList<MmsReportDataSetOrderValidationResult> ValidateAll(
        IEnumerable<MmsReportFrame> reports,
        IReadOnlyList<MmsDataSetDirectoryMember> expectedMembers)
    {
        ArgumentNullException.ThrowIfNull(reports);
        return reports.Select(report => Validate(report, expectedMembers)).ToArray();
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().Replace('$', '.');
}
