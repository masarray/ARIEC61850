# SCL Workspace API

`AR.Iec61850.Scl.Workspace.SclWorkspaceService` is the engine-owned entry point for applications that open ICD, CID, IID, SCD, SSD, or XML SCL documents.

The API keeps protocol semantics in ARIEC61850 and returns typed application-neutral results:

- secure offline XML loading with DTD and external entity processing prohibited;
- IED and AccessPoint inventory;
- direct `ConnectedAP/Address` MMS endpoint resolution;
- preservation of missing or invalid endpoint evidence instead of discarding the IED model;
- one offline `LiveIedModelDiscoveryDocument` per IED/AccessPoint;
- LD/LN/DO/DA projection from `DataTypeTemplates`;
- DataSet, ReportControl, GOOSE, and Sampled Values inventory;
- SHA-256 source identity and typed engineering findings;
- expected SCL model versus observed live MMS model comparison.

## Open an SCL workspace

```csharp
using AR.Iec61850.Scl.Workspace;

var service = new SclWorkspaceService();
var workspace = await service.OpenAsync("station.scd", cancellationToken: cancellationToken);

foreach (var ied in workspace.Ieds)
{
    Console.WriteLine($"{ied.WorkspaceKey}: {ied.DesignModel.Summary}");
    Console.WriteLine(ied.PreferredEndpoint?.EndpointText ?? "endpoint binding required");
}
```

Opening an SCL workspace is offline and does not create an MMS association. An ICD without a `Communication` section still produces a browseable design model and reports `RequiresEndpointBinding=true`.

## Select one IED or AccessPoint

```csharp
var workspace = await service.OpenAsync(
    "station.scd",
    new SclWorkspaceOpenOptions
    {
        IedName = "IED_A",
        AccessPointName = "P1"
    },
    cancellationToken);
```

## Compare with a live model

Applications can obtain a live `LiveIedModelDiscoveryDocument` through the existing MMS discovery engine and compare it with the offline SCL projection:

```csharp
var ied = workspace.Ieds.Single();
var comparison = service.CompareLive(ied, liveModel);

if (comparison.RequiresFullDiscovery)
{
    foreach (var finding in comparison.Findings)
        Console.WriteLine($"{finding.Severity}: {finding.Message}");
}
```

The comparison checks IED identity, expected data attributes, functional constraints, basic types, DataSet presence/member count, and ReportControl mode/DataSet/confRev. Unexpected live objects are retained as informational evidence; missing or incompatible expected objects are blocking findings.

## Application boundary

Applications such as ArIED should present and orchestrate this API. They should not implement independent SCL XML parsing, endpoint interpretation, DataTypeTemplates traversal, or expected-versus-live comparison logic.
