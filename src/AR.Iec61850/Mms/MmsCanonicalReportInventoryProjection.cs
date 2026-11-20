using AR.Iec61850.Engineering.Canonical;

namespace AR.Iec61850.Mms;

/// <summary>
/// Projects immutable canonical engineering structure into the legacy report-inventory shape
/// without promoting it to live runtime evidence.
///
/// This exists so applications do not need to duplicate IEC 61850 DataSet/RCB mapping logic.
/// It intentionally does not fabricate indexed RCB instances, writable attribute exposure,
/// reservation state, ownership, or live enable state.
/// </summary>
public static class MmsCanonicalReportInventoryProjection
{
    public static MmsReportInventory Build(CanonicalIedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var inventory = new MmsReportInventory
        {
            Authority = model.Source.Ingress == CanonicalIngressKind.SclFile
                ? MmsReportInventoryAuthority.SclDesignProjection
                : MmsReportInventoryAuthority.CanonicalModelProjection
        };

        foreach (var dataSet in model.DataSets)
        {
            var (domain, tail) = SplitReference(dataSet.Reference);
            inventory.DataSets.Add(new MmsDataSetCandidate
            {
                Domain = FirstNonEmpty(dataSet.MmsDomain, domain),
                LogicalNode = FirstNonEmpty(dataSet.LogicalNode, LogicalNodeFromTail(tail)),
                Name = FirstNonEmpty(dataSet.Name, LeafFromTail(tail)),
                Reference = dataSet.Reference,
                RawMmsName = string.Empty
            });
        }

        foreach (var report in model.ReportControls)
        {
            var (domain, tail) = SplitReference(report.Reference);
            inventory.ReportControls.Add(new MmsReportControlCandidate
            {
                Domain = FirstNonEmpty(report.MmsDomain, domain),
                LogicalNode = FirstNonEmpty(report.LogicalNode, LogicalNodeFromTail(tail)),
                FunctionalConstraint = report.Buffered ? "BR" : "RP",
                Name = FirstNonEmpty(report.Name, LeafFromTail(tail)),
                Reference = report.Reference,
                Buffered = report.Buffered,
                DataSetReference = report.DataSetReference,
                ReportId = report.ReportId,
                ConfRev = report.ConfRev,
                IntegrityPeriodMs = report.IntegrityPeriodMs,
                BufferTimeMs = report.BufferTimeMs,
                TriggerOptions = report.TriggerOptions,
                OptionalFields = report.OptionalFields,
                Status = "CanonicalModelProjection"
            });
        }

        return inventory;
    }

    private static (string Domain, string Tail) SplitReference(string? reference)
    {
        var normalized = (reference ?? string.Empty).Trim();
        var slash = normalized.IndexOf('/');
        return slash > 0 && slash + 1 < normalized.Length
            ? (normalized[..slash], normalized[(slash + 1)..])
            : (string.Empty, normalized);
    }

    private static string LogicalNodeFromTail(string tail)
    {
        var dot = tail.IndexOf('.');
        return dot > 0 ? tail[..dot] : string.Empty;
    }

    private static string LeafFromTail(string tail)
    {
        var dot = tail.LastIndexOf('.');
        return dot >= 0 && dot + 1 < tail.Length ? tail[(dot + 1)..] : tail;
    }

    private static string FirstNonEmpty(string? primary, string? fallback)
        => !string.IsNullOrWhiteSpace(primary)
            ? primary.Trim()
            : (fallback ?? string.Empty).Trim();
}
