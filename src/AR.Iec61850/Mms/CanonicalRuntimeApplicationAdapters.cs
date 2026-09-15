using AR.Iec61850.Engineering.Runtime;

namespace AR.Iec61850.Mms;

/// <summary>
/// Application producer adapters for MMS paths whose established public result contracts
/// carry already-rendered values rather than the original MMS Data object. These adapters
/// preserve the last good canonical value on failed reads and bind successful evidence to
/// the currently published model generation.
/// </summary>
public static class CanonicalMmsRuntimeApplicationAdapter
{
    public static CanonicalRuntimeAdapterResult ApplyPollRead(
        CanonicalRuntimeSnapshotPublisher publisher,
        MmsReportPollRead poll,
        string source = "poll")
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(poll);

        if (!poll.IsSuccess || string.IsNullOrWhiteSpace(poll.SelectedReference))
        {
            return new CanonicalRuntimeAdapterResult
            {
                InputUpdateCount = 1,
                UnresolvedUpdateCount = 1,
                Diagnostics = [string.IsNullOrWhiteSpace(poll.Message)
                    ? "Persistent monitor poll did not return a usable canonical value."
                    : poll.Message]
            };
        }

        var current = publisher.Current;
        if (current is null)
        {
            return new CanonicalRuntimeAdapterResult
            {
                InputUpdateCount = 1,
                UnresolvedUpdateCount = 1,
                Diagnostics = ["Canonical runtime model is not published; poll evidence was not applied."]
            };
        }

        var applied = current.Values.Apply(new CanonicalRuntimeValueUpdate
        {
            Reference = poll.SelectedReference,
            FunctionalConstraint = poll.FunctionalConstraint,
            Value = poll.DisplayValue,
            Source = string.IsNullOrWhiteSpace(source) ? "poll" : source,
            UpdatedAtUtc = poll.ReadAt == default ? DateTimeOffset.UtcNow : poll.ReadAt,
            HasValue = true
        });

        return new CanonicalRuntimeAdapterResult
        {
            InputUpdateCount = 1,
            AppliedSignalCount = applied.AppliedSignalCount,
            UnresolvedUpdateCount = applied.IsApplied ? 0 : 1,
            Diagnostics = applied.IsApplied ? Array.Empty<string>() : [applied.Message]
        };
    }
}
