# IEC 61850 Smart Control Stack

## Purpose

`AR.Iec61850.Control` is the native client-side control-object service for ARIEC61850. It turns a controllable Data Object such as `LD0/CSWI1.Pos` into a typed, stateful control session. Applications no longer assemble an MMS `Oper`, `SBOw`, or `Cancel` structure by hand.

This implementation is source- and unit-test-level engineering work. It is not yet a conformance claim. Live multi-vendor evidence is still required before product software should enable unrestricted field control.

## Why this is separate from generic MMS write

IEC 61850 control is a sequence, not a single arbitrary write. The client has to discover the live control model and the exact server-defined MMS type, preserve sequence identity, and distinguish immediate service acceptance from final command completion.

The stack therefore blocks public generic writes to these `CO` members:

- `Oper`
- `SBOw`
- `Cancel`

Only the native control service can write them.

## Public API

```csharp
using AR.Iec61850.Control;

var controlService = new Iec61850ControlService();

await using var control = await controlService.OpenAsync(
    mmsSession,
    "LD0/CSWI1.Pos",
    cancellationToken);

var result = await control.OperateAsync(
    new Iec61850ControlRequest
    {
        ControlValue = Iec61850ControlValue.Close(),
        Origin = Iec61850Origin.FromText(
            "ARIED",
            Iec61850OriginCategory.Maintenance),
        InterlockCheck = true,
        SynchroCheck = true,
        Test = false,
        AutoSelect = true
    },
    cancellationToken);
```

`OperateAsync` is the smart entry point. It automatically performs Select or SelectWithValue when the discovered `ctlModel` requires it.

## Supported control sequences

| `ctlModel` | Sequence executed by the stack | Completion boundary |
|---|---|---|
| Direct normal | `Oper` | confirmed MMS service result |
| SBO normal | `SBO` read, then `Oper` | confirmed MMS service result |
| Direct enhanced | `Oper`, then CommandTermination | positive or negative termination |
| SBO enhanced | `SBOw`, then `Oper`, then CommandTermination | positive or negative termination |

For enhanced security, a successful MMS write is not reported as final success. `Iec61850ControlActionResult` keeps `RequestAccepted`, `CommandTerminationReceived`, and `PositiveTermination` separate.

## Discovery contract

Opening a control session performs live discovery:

1. Parse and validate the Data Object root.
2. Read `CF/ctlModel`.
3. Retrieve the live `CO/Oper` variable-access specification.
4. Locate `ctlVal` by its component name, never by positional guessing.
5. Retrieve and compare `SBOw` type information for SBO enhanced.
6. Retrieve `Cancel` type information for SBO models.
7. Discover `sboTimeout`, `operTimeout`, and a likely process status reference when exposed.
8. Reject the descriptor if a safe native sequence cannot be formed.

Raw leaves such as `ctlModel`, `ctlVal`, `Oper`, `SBOw`, or `Cancel` are rejected as control-object roots.

## Exact live value binding

The command binder maps application intent to the live MMS `ctlVal` specification:

- SPC: Boolean On/Off.
- DPC: exact two-bit Open/Close representation.
- INC/ISC: signed or unsigned integer values, including Raise/Lower helpers.
- BSC: named `ValWithTrans` components.
- APC: live integer or floating `AnalogueValue` member.
- Vendor-specific values: explicit `Iec61850ControlValue.Raw(...)`, still validated against the live specification.

Integer and unsigned values are not treated as interchangeable. A raw value with the wrong MMS kind is rejected before it reaches the network.

## Immutable sequence identity

One sequence context owns:

- bound `ctlVal`;
- origin category and identifier;
- `ctlNum`;
- `T`;
- Test;
- interlock-check and synchrocheck bits;
- optional `operTm`.

The same values are retained from `SBOw` through `Oper`. A caller cannot mutate the command after selection. A mismatched Operate request triggers best-effort `Cancel` and returns a rejected result.

`ctlNum` is generated per MMS association and control object. Lock state and counters are attached to association identity through a weak table, avoiding cross-association collision and long-lived static ownership.

## Concurrency, timeout, and cleanup

- Only one local sequence can own one control object on one association; calls on the same control session are serialized.
- SBO selection timeout is enforced by an automatic local lease; expiry performs best-effort `Cancel` and releases local ownership even when the association is already lost.
- Disposal cancels an active selection when the association is still available.
- Caller cancellation, command timeout, and association loss are separate result states.
- Time-activated operations extend the termination wait window by the remaining activation delay.
- A bounded InformationReport fan-out keeps command termination independent from the legacy report queue and the confirmed-request receive path.

## Command termination and application error decoding

The stack subscribes before sending enhanced-security control traffic so an immediate asynchronous response is not missed. It decodes:

- positive CommandTermination on the exact `CO/Oper` reference;
- negative CommandTermination / LastApplError;
- control error;
- AddCause values through `locked-by-other-client`;
- request and response hex evidence.

An ordinary ST or MX process report cannot complete an enhanced command because positive completion requires the matching Oper reference.

## Application integration gate

A product should enable its Send Command control only when:

```csharp
control.Descriptor.IsOperationallyReady
```

The descriptor also exposes:

- `ControlModel`
- `Cdc`
- exact `CtlValSpecification`
- `StatusReference`
- `SboTimeout`
- `OperTimeout`
- time-activated support
- command-termination support
- discovery evidence

The application should still require a deliberate lab/commissioning confirmation before live operation.

The IED Discovery application implements this gate with a compact live-command arming checkbox, a per-command confirmation dialog, current-state suppression, and an advanced evidence panel. See [`IED_DISCOVERY_SMART_CONTROL_TESTER.md`](IED_DISCOVERY_SMART_CONTROL_TESTER.md).

## Automated test coverage

`SmartControlStackTests` covers:

- root validation and exact FC references;
- DPC, SPC, INC/ISC, BSC, APC integer and floating binding;
- constant `ctlNum` and `T` across SBOw/Oper;
- Test, interlock, synchrocheck, UTC time, and MMS 1984 binary-time epoch;
- direct normal and SBO normal execution;
- direct enhanced positive and negative termination;
- asynchronous SBOw LastApplError;
- SBO enhanced sequence and missing termination timeout;
- selection mutation cancellation;
- explicit and automatic selection timeout cleanup and Cancel;
- association loss;
- concurrent clients on one object;
- exact descriptor discovery;
- generic MMS control-write blocking;
- InformationReport subscriber fan-out and association reset faulting.

## Required live validation before production claim

1. DPC Open/Close on all four control models.
2. SPC On/Off.
3. INC/ISC and BSC vendor variants.
4. APC integer and floating variants.
5. Test-mode operation with no process movement.
6. Interlock and synchrocheck rejection evidence.
7. Positive and negative CommandTermination captures.
8. SBO expiry and explicit Cancel.
9. Association loss during an active sequence.
10. Competing external client ownership.
11. Process feedback correlation using the discovered status reference.
12. Long-duration repeated operation with request/response/termination evidence.

Keep the product read-only when any required live type or sequence evidence is missing.
