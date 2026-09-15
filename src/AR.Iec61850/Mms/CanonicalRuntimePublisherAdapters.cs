using AR.Iec61850.Engineering.Runtime;

namespace AR.Iec61850.Mms;

public enum CanonicalStep4RuntimePublicationStatus
{
    Published,
    InvalidStep4Result,
    PublicationRejected,
    PublicationTimedOut,
    Superseded
}

public sealed class CanonicalStep4RuntimePublicationResult
{
    public CanonicalStep4RuntimePublicationStatus Status { get; init; }
    public long ModelGeneration { get; init; }
    public CanonicalRuntimeAdapterResult? InitialValues { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool IsPublished => Status == CanonicalStep4RuntimePublicationStatus.Published;
}

/// <summary>
/// Connects the active canonical Step-4 result to the P1 publication boundary. Model
/// publication stays on the bounded worker; initial FC Read evidence is applied only to
/// the exact model generation that was just published.
/// </summary>
public static class CanonicalStep4RuntimePublication
{
    public static async Task<CanonicalStep4RuntimePublicationResult> PublishAsync(
        CanonicalRuntimeSnapshotPublisher publisher,
        CanonicalSclStep4ExecutionResult step4,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(step4);

        if (!step4.Design.IsValid || step4.Status is
            CanonicalSclStep4ExecutionStatus.InvalidCanonicalModel or
            CanonicalSclStep4ExecutionStatus.InvalidAssociationPlan or
            CanonicalSclStep4ExecutionStatus.DomainMismatch or
            CanonicalSclStep4ExecutionStatus.OnlineValidationFailed)
        {
            return new CanonicalStep4RuntimePublicationResult
            {
                Status = CanonicalStep4RuntimePublicationStatus.InvalidStep4Result,
                Message = $"Step-4 result '{step4.Status}' is not eligible for canonical runtime publication."
            };
        }

        var previousGeneration = publisher.Current?.ModelGeneration ?? 0;
        if (!publisher.TryPublish(step4.Design.Model))
        {
            return new CanonicalStep4RuntimePublicationResult
            {
                Status = CanonicalStep4RuntimePublicationStatus.PublicationRejected,
                Message = "Canonical runtime publisher rejected the Step-4 model publication."
            };
        }

        var deadline = DateTime.UtcNow + (timeout is { } requested && requested > TimeSpan.Zero
            ? requested
            : TimeSpan.FromSeconds(5));
        CanonicalRuntimePublishedSnapshot? current = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = publisher.Current;
            if (current is not null && current.ModelGeneration > previousGeneration)
                break;
            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
        }

        if (current is null || current.ModelGeneration <= previousGeneration)
        {
            return new CanonicalStep4RuntimePublicationResult
            {
                Status = CanonicalStep4RuntimePublicationStatus.PublicationTimedOut,
                Message = "Timed out waiting for the bounded canonical runtime publisher to expose the Step-4 model."
            };
        }

        var expectedIdentity = step4.Design.Model.Identity.Name;
        if (!string.Equals(current.ModelSnapshot.Model.Identity.Name, expectedIdentity, StringComparison.Ordinal) ||
            current.ModelSnapshot.Model.SignalCount != step4.Design.Model.SignalCount)
        {
            return new CanonicalStep4RuntimePublicationResult
            {
                Status = CanonicalStep4RuntimePublicationStatus.Superseded,
                ModelGeneration = current.ModelGeneration,
                Message = "A different canonical model superseded the Step-4 publication before initial values could be attached."
            };
        }

        var initialValues = step4.InitialRead is null
            ? null
            : CanonicalMmsRuntimeValueAdapter.ApplyInitialFcRead(current.Values, step4.InitialRead);

        return new CanonicalStep4RuntimePublicationResult
        {
            Status = CanonicalStep4RuntimePublicationStatus.Published,
            ModelGeneration = current.ModelGeneration,
            InitialValues = initialValues,
            Message = initialValues is null
                ? $"Published canonical Step-4 model generation {current.ModelGeneration}; no initial Read projection was available."
                : $"Published canonical Step-4 model generation {current.ModelGeneration}. {initialValues.Summary}"
        };
    }
}

/// <summary>
/// Convenience router for long-lived MMS producers. Every operation resolves the current
/// published model/value pair at call time, so stale planes cannot be updated after a
/// canonical model generation change.
/// </summary>
public static class CanonicalMmsRuntimePublisherAdapter
{
    public static CanonicalRuntimeAdapterResult ApplyReportProjection(
        CanonicalRuntimeSnapshotPublisher publisher,
        MmsReportValueProjection projection)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        var current = publisher.Current;
        return current is null
            ? MissingModel("report")
            : CanonicalMmsRuntimeValueAdapter.ApplyReportProjection(current.Values, projection);
    }

    public static CanonicalRuntimeAdapterResult ApplyRead(
        CanonicalRuntimeSnapshotPublisher publisher,
        string reference,
        string functionalConstraint,
        MmsReadResult read,
        string source = "poll")
    {
        ArgumentNullException.ThrowIfNull(publisher);
        var current = publisher.Current;
        return current is null
            ? MissingModel(source)
            : CanonicalMmsRuntimeValueAdapter.ApplyRead(current.Values, reference, functionalConstraint, read, source);
    }

    private static CanonicalRuntimeAdapterResult MissingModel(string source)
        => new()
        {
            UnresolvedUpdateCount = 1,
            Diagnostics = [$"Canonical runtime model is not published; '{source}' evidence was not applied."]
        };
}
