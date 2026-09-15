# Canonical Runtime Value Plane

## Purpose

The canonical engineering model is an immutable description of IED identity, topology, type evidence, DataSets, report controls, and signal identity. Runtime values must not cause that static model to be rebuilt.

The runtime boundary is:

```text
Protocol / SCL worker
        |
        v
Canonical immutable model snapshot
        |
        +------------------------------+
        |                              |
        v                              v
indexed model/query projection   runtime value plane
                                 SignalId -> value
                                          -> quality
                                          -> timestamp
                                          -> reason
                                          -> source
                                          -> value version
        |                              |
        +---------------+--------------+
                        v
                 bounded consumer page
                  /        |        \
                 UI       CLI      exporter
```

## Model-generation binding

A `CanonicalRuntimeValuePlane` belongs to exactly one `CanonicalPublishedSnapshot` generation. Canonical `SignalId` is dense and is used directly as the array ordinal. A value plane must be replaced when a different canonical model generation is published; runtime state is never silently projected onto a different signal table.

The immutable model remains the authority for reference, FC, CDC/type evidence, provenance, DataSet membership, and report-control metadata. The value plane stores only changing runtime evidence.

## Runtime state

For each canonical signal the value plane can retain:

- process/display value;
- quality;
- IED timestamp or projected timestamp display;
- reason-for-inclusion or other runtime reason evidence;
- source (`initial-read`, `report`, `poll`, or another explicit producer);
- client update time; and
- monotonically increasing runtime value version.

Partial updates are merged. A quality-only or timestamp-only report therefore does not destroy the last process value.

Repeated runtime strings are interned in a per-plane pool. High-cardinality runtime rows are primitive arrays rather than one managed object per signal. The fixed primitive-array payload is 40 bytes per signal before pooled strings and synchronization objects.

## Producer adapters

Existing MMS decoders remain authoritative. The runtime adapter consumes only already-decoded evidence:

- Step-4 FC-root leaf projections;
- decoded report value projections; and
- successful ordinary MMS Reads used by polling/manual read paths.

No protocol service is reinterpreted by the value plane.

A report projector may emit a companion-only quality/timestamp update at a DataObject reference rather than at one value leaf. If no exact canonical signal exists at that reference, the plane may enrich non-`q`/`t` child signals under that DataObject. This fan-out is bounded to 256 signals and fails closed when the bound would be exceeded. No new signal identity is invented.

## Consumer contract

Consumers page a combined canonical-model plus runtime-value projection. They do not own the protocol queue and do not build a second full signal graph.

Default hard caps are:

| Consumer | Maximum rows per page |
|---|---:|
| UI | 1,000 |
| CLI | 5,000 |
| Exporter | 10,000 |

CSV export streams pages. The canonical model generation is fixed by the plane. Runtime values remain live, so the exporter records both the value generation at export start and at export end. If those values differ, the export explicitly reports that runtime values changed while the file was written rather than claiming an immutable value snapshot.

## Safety and scale invariants

- Runtime updates never mutate `CanonicalIedModel`.
- A value plane cannot be reused for a different canonical model generation.
- Unknown references remain unresolved; they are not added heuristically.
- Companion fan-out is bounded and excludes `q` and `t` carrier rows.
- Consumer page sizes are bounded.
- Runtime text is pooled instead of duplicated per signal.
- One million canonical signals must keep the fixed runtime primitive-array overlay below the 64 MiB regression budget.

## Remaining application cutover

The shared runtime contract is the intended source for UI, CLI, and export presentation. Existing application-specific view models may be migrated incrementally, but new value-refresh paths should update this plane rather than rebuilding static model trees. Presentation code should request bounded pages and refresh only visible/pinned rows.
