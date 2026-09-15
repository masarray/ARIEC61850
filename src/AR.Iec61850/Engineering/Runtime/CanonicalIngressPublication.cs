using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Engineering.Canonical;
using AR.Iec61850.Scl.Engineering;

namespace AR.Iec61850.Engineering.Runtime;

/// <summary>
/// Producer-side bridge for the P0 publication pipeline. Protocol and SCL workers stop at
/// CanonicalIedModel and publish that immutable semantic snapshot into the bounded latest-
/// only pipeline. UI/CLI/exporter consumers never receive parser DOMs or discovery graphs.
/// </summary>
public static class CanonicalIngressPublication
{
    public static bool TryPublishLiveDiscovery(
        CanonicalSnapshotPublisher publisher,
        LiveIedModelDiscoveryDocument discovery)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(discovery);
        return publisher.TryPublish(CanonicalLiveModelAdapter.FromLiveDiscovery(discovery));
    }

    public static bool TryPublishScl(
        CanonicalSnapshotPublisher publisher,
        XDocument document,
        SclCanonicalImportOptions? options,
        out SclCanonicalImportResult import)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(document);

        import = SclCanonicalImporter.Import(document, options);
        return import.IsSuccess && import.Model is not null && publisher.TryPublish(import.Model);
    }

    public static bool TryPublishSclFile(
        CanonicalSnapshotPublisher publisher,
        string filePath,
        SclCanonicalImportOptions? options,
        out SclCanonicalImportResult import)
    {
        ArgumentNullException.ThrowIfNull(publisher);

        import = SclCanonicalImporter.Load(filePath, options);
        return import.IsSuccess && import.Model is not null && publisher.TryPublish(import.Model);
    }
}
