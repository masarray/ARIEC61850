# P1B — Application Consumer Cutover

P1B moves live-value presentation in the IED Discovery application onto the canonical runtime boundary introduced by P1. The static IEC 61850 topology/type model remains immutable; changing process values live only in the runtime value plane keyed by canonical `SignalId`.

## Runtime ownership

The application owns one `CanonicalRuntimeSnapshotPublisher` for the loaded/discovered IED model. `LiveIedModelDiscoveryDocument` remains an ingress/engineering artifact, but it is not the live value store.

The active value path is:

```text
live discovery / open SCL
        |
        v
CanonicalIedModel
        |
        v
CanonicalRuntimeSnapshotPublisher
        |
        +-- immutable model + query index
        |
        +-- CanonicalRuntimeValuePlane
                 ^
                 |
       +---------+---------+
       |         |         |
  manual Read  report   fallback/pinned poll
                 |
                 v
       bounded application projection
          |                 |
      visible detail     pinned monitor
          |
          +------ streaming CSV export
```

A newly accepted model publication invalidates the previously visible runtime generation immediately. A versioned publication request prevents an older model that was already being indexed from becoming visible after a newer model has been accepted. During replacement, producers therefore see either no runtime source or the newest published generation; they never bind new values to a superseded `SignalId` table.

Unloading or clearing the active application model calls `CanonicalRuntimeSnapshotPublisher.Clear()`. Clear invalidates both the visible generation and every already accepted in-flight publication request, so values from the previous IED cannot remain queryable or exportable after the application shows no loaded model. A later discovery/open operation publishes a fresh generation with an empty value plane.

## Producer cutover

The IED Discovery application routes these live sources through canonical runtime adapters before presentation changes:

- manual MMS Read results;
- decoded report value projections;
- persistent-report fallback poll reads; and
- normal pinned-signal polling when no report monitor is active.

A failed poll/read does not replace the last known good canonical process value with an error string. Failure is presentation/diagnostic state; the last good value remains in the value plane.

Existing MMS decoding, report mapping, quality decoding, timestamp decoding, and report lifecycle code remain authoritative. P1B does not create a second protocol decoder.

## Consumer cutover

The application detaches the legacy recurring monitor timer handler at runtime and installs the canonical runtime refresh path. The toolbar `Read` and `Export` actions are also rewired to canonical runtime handlers after the WPF visual tree is ready.

Presentation refresh is generation-driven:

- the runtime `ValueGeneration` is checked before refreshing values;
- a changed pinned/visible selection also triggers a refresh even when values have not changed;
- pinned monitor resolution is capped at 256 exact signal identities;
- visible detail resolution is capped at 1,000 exact signal identities; and
- selection lookup uses the canonical reference-sorted query index, so each exact lookup starts with a binary lower bound rather than scanning the full signal inventory.

Only visible detail rows and explicitly pinned monitor rows are projected. The application does not construct or retain a second full signal graph.

## Export

The application `Export` action now writes the canonical runtime CSV stream. The exporter records model generation and start/end value generation, so a live export can report whether values changed while rows were streamed instead of claiming false snapshot consistency.

`Save SCL` remains a separate engineering-model operation and is not replaced by runtime CSV export.

## Legacy presentation code

The previous direct `MonitorSignalRow` mutation helpers remain compiled in the original code-behind as rollback/reference code, but P1B detaches their timer path. They are no longer the active recurring report/poll value path.

The static explorer, DataSet/RCB engineering panels, report enable/disable workflow, and control tester continue to use their established engineering/session models. P1B is a live-value consumer cutover, not a rewrite of protocol control semantics.

## Scale and safety gates

P1B adds regression coverage for:

- exact pinned-signal resolution against one published model/value pair;
- hard monitor-selection bounds;
- successful persistent-monitor fallback poll routing;
- preservation of the last good value after failed poll evidence;
- fail-closed model-generation replacement so superseded runtime planes cannot accept new updates; and
- explicit unload invalidation so a cleared application cannot expose a stale or late-published runtime generation.

These tests complement the P1 one-million-signal runtime-plane allocation guard and the existing bounded latest-only publication tests.
