# SCL-assisted MMS connect — Step 4

Status: **implemented + unit tested; live IED interoperability not yet validated**.

Step 4 adds a shared, bounded initial-value read path that can be planned from either the native live MMS directory or a scoped SCL design model. It does not change the MMS association negotiation and does not perform discovery writes.

## Contract

```text
Full live discovery                    SCL-assisted connect
MmsIedModelDirectory                   trusted SCL + Step-3 domain validation
        |                                      |
        +------------+-------------------------+
                     |
             InitialFcReadPlanner
                     |
       ordered FC-root targets (LD/LN$FC)
                     |
        <= 10 variables per MMS Read
                     |
          MaximumOutstandingReads = 1
                     |
       sequential Confirmed-Read batches
                     |
          ordered AccessResult evidence
                     |
       SCL shape projection when available
```

`MaximumVariableReferencesPerRead` and MMS initiate `maxOutstandingCalling` are intentionally separate concepts. Step 4 fixes its execution policy to one in-flight initial Read at a time and does not modify association-negotiated outstanding-call limits.

## Shared FC-root planner

`InitialFcReadPlanner` produces the same target form from both inputs:

```text
<domain>/<logical-node>$<functional-constraint>
```

Examples:

```text
IED01LD0/LLN0$ST
IED01LD0/MMXU1$MX
```

The live adapter groups `MmsIedModelDirectory` points by exact domain, logical node and functional constraint. The SCL adapter groups ordered DataTypeTemplates attributes by the same identity and also attaches ordered DataObject/leaf shape for deterministic value projection.

The planner:

- uses exact case-sensitive MMS domain/item identity;
- collapses exact duplicate FC-root targets;
- rejects a requested batch size outside `1..10`;
- creates ordered batches with at most ten variable references;
- always records `MaximumOutstandingReads = 1`.

## SCL `ldName` handling and exact scope

`SclInitialFcReadDesignBuilder` scopes the design to one exact `IED/AccessPoint/Server` before reusing the offline type projector. An unrelated AccessPoint cannot contribute logical-device shape even when it reuses the same `LDevice@inst`.

The Step-4 adapter remaps each selected logical device to the exact MMS domain rule already used by Step 3:

- `LDevice@ldName` when present;
- otherwise `IED@name + LDevice@inst`.

A single selected Server that reuses the same `LDevice@inst` for conflicting MMS domains fails closed instead of silently selecting one mapping.

This prevents a design that uses explicit `ldName` from being read through the wrong MMS domain.

## Bounded multi-variable MMS Read

`MmsReadBatchCodec` adds an ordered multi-variable Confirmed-Read codec with a hard safety bound of ten references per request.

Important behavior:

- a one-variable batch is byte-compatible with the existing single-variable Read encoder;
- the request preserves caller order;
- the response preserves one `AccessResult` per requested reference;
- an individual `AccessResult.failure` remains attached to its own reference;
- missing or extra access results remain explicit evidence;
- invoke-ID validation is retained.

An individual data-access failure does not become a transport failure and does not imply that another target is absent.

## Sequential executor and timeout containment

`MmsClientSession.ExecuteInitialFcReadPlanAsync(...)` accepts only a valid plan and an already initiated MMS association.

The executor sends exactly one batch, waits for its confirmed response, decodes it, and only then sends the next batch. It never starts concurrent initial Read requests.

Each in-flight batch has an explicit deadline. The default overload uses the session timeout; callers can provide an explicit per-batch timeout through the typed overload. If an in-flight Confirmed-Read times out, Step 4 returns typed `TimedOut`, stops the sequence, and resets the association so a late response cannot become stale evidence for a later operation. A transport/session exception similarly returns typed `TransportFailure`. Caller cancellation still propagates as cancellation; if it occurs after a request has entered the confirmed-operation path, the association is reset first for the same late-response containment reason.

## SCL nested value projection

When a target carries SCL shape, `InitialFcValueProjector` maps the FC-root `MmsDataValue` back to SCL leaves only when positional structure is deterministic.

Projection rules are deliberately fail-closed:

1. FC-root value must be an MMS `Structure`;
2. returned top-level DataObject count must exactly match the SCL DataObject count for that FC;
3. nested `Structure` nodes may be recursively flattened in declared order;
4. SCL attributes with `bType="Struct"` are treated as containers, not scalar leaves;
5. the flattened terminal-leaf count for each DataObject must exactly match its SCL leaf count;
6. MMS `Array`, unknown values, or cardinality mismatches are reported as projection errors rather than guessed.

The raw batch Read result remains available even when SCL leaf projection is partial.

## Explicit non-goals

Step 4 does **not** perform:

- NamedVariable or NamedVariableList rediscovery;
- `GetVariableAccessAttributes` / GVAA;
- DataSet-directory discovery;
- automatic fallback to full discovery;
- writes or controls;
- RCB enable/reservation;
- dynamic DataSet creation/deletion;
- parallel initial Reads;
- heuristic array expansion or guessed SCL structure mapping.

## Deterministic validation

Tests cover:

1. one-variable batch byte parity with the existing Read encoder;
2. hard rejection of more than ten references;
3. ordered mixed success/failure `AccessResult` decoding;
4. 23 FC roots becoming `10 + 10 + 3` batches;
5. `MaximumOutstandingReads = 1`;
6. identical FC-root form from live-directory and SCL inputs;
7. explicit `LDevice@ldName` propagation into the Step-4 SCL design;
8. exact AccessPoint scoping when `LDevice@inst` repeats elsewhere;
9. conflicting duplicate `LDevice@inst` mappings fail closed;
10. preservation of SCL terminal-leaf order;
11. `bType="Struct"` containers are not counted as scalar leaves;
12. exact nested-structure projection;
13. array projection rejection instead of guessing;
14. invalid plan rejection before any network side effect;
15. explicit per-batch timeout configuration is preserved without network side effects for invalid plans.

Claim level is **implemented + unit tested** after repository source/provenance verification, Release build, and full test-suite validation. Live IEC 61850 IED execution remains a separate evidence gate and is intentionally not claimed here.
