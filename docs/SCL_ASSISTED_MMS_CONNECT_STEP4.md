# SCL-assisted MMS connect — Step 4

Status: **implemented; deterministic CI validation pending; live IED interoperability not yet validated**.

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

## SCL `ldName` handling

`SclInitialFcReadDesignBuilder` scopes the design to one exact `IED/AccessPoint/Server`.

The existing offline SCL type projector supplies ordered LN/DO/DA shape. The Step-4 adapter then remaps each selected logical device to the exact MMS domain rule already used by Step 3:

- `LDevice@ldName` when present;
- otherwise `IED@name + LDevice@inst`.

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

## Sequential executor

`MmsClientSession.ExecuteInitialFcReadPlanAsync(...)` accepts only a valid plan and an already initiated MMS association.

The executor sends exactly one batch, waits for its confirmed response, decodes it, and only then sends the next batch. It never starts concurrent initial Read requests.

A transport/session exception stops the sequence, resets the protocol transport through the existing fault path, and returns a typed `TransportFailure`. Caller cancellation still propagates as cancellation.

## SCL nested value projection

When a target carries SCL shape, `InitialFcValueProjector` maps the FC-root `MmsDataValue` back to SCL leaves only when positional structure is deterministic.

Projection rules are deliberately fail-closed:

1. FC-root value must be an MMS `Structure`;
2. returned top-level DataObject count must exactly match the SCL DataObject count for that FC;
3. nested `Structure` nodes may be recursively flattened in declared order;
4. the flattened leaf count for each DataObject must exactly match its SCL leaf count;
5. MMS `Array`, unknown values, or cardinality mismatches are reported as projection errors rather than guessed.

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

## Deterministic validation added

Tests cover:

1. one-variable batch byte parity with the existing Read encoder;
2. hard rejection of more than ten references;
3. ordered mixed success/failure `AccessResult` decoding;
4. 23 FC roots becoming `10 + 10 + 3` batches;
5. `MaximumOutstandingReads = 1`;
6. identical FC-root form from live-directory and SCL inputs;
7. explicit `LDevice@ldName` propagation into the Step-4 SCL design;
8. preservation of SCL leaf order;
9. exact nested-structure projection;
10. array projection rejection instead of guessing;
11. invalid plan rejection before any network side effect.

Repository CI must pass on the exact Step-4 head before the claim is raised to **unit tested**. Live IEC 61850 IED execution remains a separate evidence gate even after CI is green.
