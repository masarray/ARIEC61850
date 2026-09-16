# Smart MMS discovery

`MmsClientSession.DiscoverSmartAsync` is the capture-informed discovery path for fast, deterministic IEC 61850 model construction.

## Design invariants

- One TCP/COTP/MMS association and one receive pump per session.
- Confirmed responses are correlated by invoke ID; discovery never starts a second raw receive loop.
- The VMD domain list is enumerated once per discovery operation.
- Independent `(domain, object-class)` GetNameList chains may be outstanding concurrently.
- Continuation pages inside one GetNameList chain are always sequential.
- The effective discovery window is capped by the peer's negotiated `maxOutstandingCalling` when available. Unknown peers use a conservative fallback cap.
- Published dictionaries are rebuilt in sorted domain order, so concurrent completion never changes model ordering.
- A failed domain chain produces an empty branch while successful branches remain available in the returned structural model.
- Initial smart discovery does not perform an eager per-leaf Read or GetVariableAccessAttributes sweep.

## Type enrichment

`GetVariableAccessAttributesSmartAsync` performs structure-first type discovery:

1. Group discovered FC points by MMS `LN$FC$DO` root.
2. Probe each root with GetVariableAccessAttributes using the same bounded association-aware window.
3. When a root returns a structured TypeSpecification, its hierarchy represents descendant attributes.
4. When the root probe fails or does not describe a hierarchy, fall back to exact leaf probes for that root only.

This keeps exact metadata available while avoiding one GetVariableAccessAttributes request per leaf on normal IEC 61850 MMS models.

## Transport safety

Pipelining requires multiple confirmed requests to be outstanding. `TpktClient` therefore serializes writers around each complete TPKT frame. This is intentionally a write-frame gate only: it prevents byte interleaving without serializing the request/response lifecycle. The existing single receive pump remains the only association reader.

## Suggested usage

```csharp
var discovery = await session.DiscoverSmartAsync(
    new MmsSmartDiscoveryOptions
    {
        MaxConcurrentChains = 8,
        ProbeReportAttributes = true,
        ReadDataSetDirectories = true
    },
    cancellationToken);

var exactTypes = await session.GetVariableAccessAttributesSmartAsync(
    discovery.IedDirectory,
    cancellationToken: cancellationToken);
```

The legacy `DiscoverAsync` and `GetVariableAccessAttributesBatchAsync` APIs remain unchanged for compatibility. Consumers can migrate deliberately and compare model completeness before making smart discovery their default.

## Capture-informed target

The reference capture used during this refactor showed the existing consumer issuing roughly 30.7k confirmed MMS requests, including roughly 23.7k GetVariableAccessAttributes and 6.6k Read requests, while the comparison tool used a much smaller, pipelined request set. These values are benchmark evidence, not protocol requirements. The smart path targets the scheduling pattern—single association, bounded outstanding requests, structural discovery first—without copying vendor-specific behavior.

For acceptance, compare the same IED and capture conditions using:

- time to first visible model item;
- time to usable LD/LN/DO/DA tree;
- final LD/LN/FC-point and dataset counts;
- confirmed request count by service;
- peak outstanding confirmed requests;
- failed/partial domain chains;
- exact type coverage after smart enrichment.

A performance result is accepted only when the final model remains semantically equivalent for the required scope.
