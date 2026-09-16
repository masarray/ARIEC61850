# Smart MMS discovery

`MmsClientSession.DiscoverSmartAsync` is the capture-informed discovery path for fast, deterministic IEC 61850 model construction. It follows the repository's canonical contract: live MMS provides online evidence, SCL may prioritize and validate that evidence, and both feed the same canonical model rather than separate semantic trees.

## Design invariants

- One TCP/COTP/MMS association and one receive pump per session.
- Confirmed responses are correlated by invoke ID; discovery never starts a second raw receive loop.
- The VMD domain list is enumerated once per discovery operation.
- Independent `(domain, object-class)` GetNameList chains may be outstanding concurrently.
- Continuation pages inside one GetNameList chain are always sequential.
- The effective discovery window is capped by the peer's negotiated `maxOutstandingCalling` when available. Unknown peers use a conservative fallback cap.
- A fixed-size worker pool is used instead of creating one Task per domain/service pair, keeping scheduling and allocation bounded on large IEDs.
- Published dictionaries are rebuilt from live evidence in deterministic domain order, so concurrent completion and SCL priority hints never change model semantics.
- Valid evidence from completed pages/chains is retained when a later page or another chain fails.
- Expected receive-pump/association loss during concurrent work becomes a partial result instead of an unhandled `Task.WhenAll` failure.
- Initial smart discovery does not perform an eager per-leaf Read or GetVariableAccessAttributes sweep.
- Report attribute reads and DataSet directory reads are deferred by default because they are enrichment, not prerequisites for the first usable LD/LN/DO/DA tree.

## Bounded pagination

The smart pager is intentionally separate from the compatibility pager. It uses an O(n) `HashSet` boundary de-duplication path and has explicit guards for:

- maximum pages per GetNameList chain;
- maximum names per chain;
- no-new-name pages while `moreFollows` remains true;
- empty continuation tokens;
- repeated continuation tokens/cycles.

If a guard fires, already-decoded names remain available and the chain is marked incomplete. The engine does not keep asking the same IED question indefinitely.

## Type enrichment

Canonical type enrichment is planned by `LiveIedVariableTypeProbePlanner` and executed by `LiveIedVariableTypeProbeExecutor.ProbeSmartAsync`.

The adaptive ladder is:

1. Probe each MMS logical-node root (`LN`) once.
2. Use the returned `TypeSpecification` tree to prove every descendant path that is actually present in that hierarchy.
3. Only unresolved branches fall back to distinct `LN$FC$DO` roots.
4. Only descendants still unresolved after the DO-root result fall back to exact leaf GVA.

This is deliberately coverage-aware: a successful but shallow parent response does not suppress required child probes. Normal structured IEC 61850 servers therefore approach one GVA per logical node, while unusual servers still retain an exact fallback path.

## SCL-assisted scheduling

SCL is a scheduling/validation hint, never a substitute for online evidence. `MmsSmartDiscoveryOptions.PriorityDomains` can be populated from the exact expected MMS domains of the trusted SCL IED/AP/Server selection. The engine still performs live VMD `GetNameList`, keeps extra live domains, and publishes the live-selected domain set unchanged.

```csharp
var options = new MmsSmartDiscoveryOptions
{
    MaxConcurrentChains = 8,
    // Optional, when trusted SCL context is already selected:
    PriorityDomains = sclDomainInventory?.ExpectedDomains ?? Array.Empty<string>()
};

var discovery = await session.DiscoverSmartAsync(options, cancellationToken);

var exactTypes = await LiveIedVariableTypeProbeExecutor.ProbeSmartAsync(
    session,
    discovery.IedDirectory,
    options,
    cancellationToken);
```

Expensive runtime enrichment can be requested explicitly when needed:

```csharp
var deepOptions = new MmsSmartDiscoveryOptions
{
    MaxConcurrentChains = 8,
    ProbeReportAttributes = true,
    ReadDataSetDirectories = true
};
```

The legacy `DiscoverAsync` and `GetVariableAccessAttributesBatchAsync` APIs remain unchanged for compatibility. Consumers can migrate deliberately and compare model completeness before making smart discovery their default.

## Transport safety

Pipelining requires multiple confirmed requests to be outstanding. `TpktClient` therefore serializes writers around each complete TPKT frame. The frame is allocated inside that single-writer gate, keeping peak outbound-frame allocation bounded while still allowing multiple request/response lifecycles to remain outstanding. The existing single receive pump remains the only association reader.

## Capture-informed target

The reference capture used during this refactor showed the existing consumer issuing roughly 30.7k confirmed MMS requests, including roughly 23.7k GetVariableAccessAttributes and 6.6k Read requests, while the comparison tool used a much smaller, pipelined request set. These values are benchmark evidence, not protocol requirements. The smart path targets the independently observed scheduling pattern—single association, bounded outstanding requests, structural discovery first, hierarchy-aware metadata, selective reads—without copying vendor code or vendor-specific implementation details.

For acceptance, compare the same IED and capture conditions using:

- time to first visible model item;
- time to usable LD/LN/DO/DA tree;
- final LD/LN/FC-point and dataset counts;
- confirmed request count by service;
- peak outstanding confirmed requests;
- failed/partial domain chains;
- exact type coverage after smart enrichment;
- managed allocations and task count during discovery;
- UI-thread stalls / long frames in the consuming application.

A performance result is accepted only when the final model remains semantically equivalent for the required scope.
