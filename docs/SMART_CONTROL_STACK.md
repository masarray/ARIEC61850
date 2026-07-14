# IEC 61850 Control Stack

## Purpose

`AR.Iec61850.Control` is the typed client-side control-object service for ARIEC61850. It turns a controllable Data Object such as `LD0/CSWI1.Pos` into a stateful control session. Applications do not assemble an MMS `Oper`, `SBOw`, or `Cancel` structure by hand.

The implementation is supported by source and automated tests, plus limited laboratory evidence. It is not a formal conformance claim, universal interoperability claim, functional-safety assessment, or permission for unrestricted field operation.

## Why control is separate from generic MMS write

IEC 61850 control is a sequence, not a single arbitrary write. The client must discover the live control model and exact server-defined MMS type, preserve sequence identity, and distinguish immediate service acceptance from final command completion.

The public generic-write path therefore blocks these `CO` members:

- `Oper`
- `SBOw`
- `Cancel`

Only the typed control service can write them.

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

`OperateAsync` is the high-level entry point. It performs Select or SelectWithValue when the discovered `ctlModel` requires it.

## Supported control sequences

| `ctlModel` | Sequence | Completion boundary |
|---|---|---|
| Direct normal | `Oper` | confirmed MMS service result |
| SBO normal | `SBO` read, then `Oper` | confirmed MMS service result |
| Direct enhanced | `Oper`, then CommandTermination | positive or negative termination |
| SBO enhanced | `SBOw`, then `Oper`, then CommandTermination | positive or negative termination |

For enhanced security, successful MMS acceptance is not reported as final completion. `Iec61850ControlActionResult` keeps `RequestAccepted`, `CommandTerminationReceived`, and `PositiveTermination` separate.

## Discovery contract

Opening a control session performs live discovery:

1. Parse and validate the Data Object root.
2. Read `CF/ctlModel`.
3. Retrieve the live `CO/Oper` variable-access specification.
4. Locate `ctlVal` by component name rather than positional assumption.
5. Retrieve and compare `SBOw` type information for SBO enhanced.
6. Retrieve `Cancel` type information for SBO models.
7. Discover `sboTimeout`, `operTimeout`, and a likely process-status reference when exposed.
8. Reject the descriptor when a typed sequence cannot be formed from the available evidence.

Raw leaves such as `ctlModel`, `ctlVal`, `Oper`, `SBOw`, or `Cancel` are rejected as control-object roots.

## Exact live value binding

The command binder maps application intent to the live MMS `ctlVal` specification:

- SPC: Boolean On/Off.
- DPC: exact two-bit Open/Close representation.
- INC/ISC: signed or unsigned values, including Raise/Lower helpers.
- BSC: named `ValWithTrans` components.
- APC: live integer or floating `AnalogueValue` member.
- implementation-specific values: explicit `Iec61850ControlValue.Raw(...)`, validated against the live specification.

Integer and unsigned values are not treated as interchangeable. A value with the wrong MMS kind is rejected before network transmission.

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

`ctlNum` is generated per MMS association and control object. Lock state and counters are attached to association identity to avoid cross-association collision and long-lived static ownership.

## Concurrency, timeout, and cleanup

- Only one local sequence can own one control object on one association.
- Calls on the same control session are serialized.
- SBO selection timeout is enforced by a local lease.
- Lease expiry performs best-effort `Cancel` and releases local ownership.
- Disposal attempts cancellation when the association remains available.
- Caller cancellation, command timeout, association loss, service rejection, and negative termination remain distinct outcomes.
- Time-activated operations extend the termination wait window by the remaining activation delay.
- InformationReport fan-out keeps command termination separate from the legacy report queue and confirmed-request receive path.

Best-effort cleanup is not a guarantee that a remote IED accepted or completed cleanup. The resulting evidence must remain visible.

## Command termination and application errors

The stack subscribes before sending enhanced-security traffic so an immediate asynchronous response is not missed. It decodes:

- positive CommandTermination on the matching `CO/Oper` reference;
- negative CommandTermination and LastApplError;
- control error;
- AddCause values;
- request and response evidence.

An ordinary ST or MX process report cannot complete an enhanced command because positive completion requires the matching Oper reference.

## Application enablement gate

An application should expose a live command only when:

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

`IsOperationallyReady` means the engine has enough protocol evidence to form a typed request. It does not prove that the connected circuit, interlocking, switching procedure, or equipment is safe.

The application must still require deliberate confirmation after the operator verifies authority, isolation, blocking, test conditions, and independent indications.

## Automated coverage

`SmartControlStackTests` covers:

- root validation and exact FC references;
- DPC, SPC, INC/ISC, BSC, APC integer and floating binding;
- constant `ctlNum` and `T` across SBOw/Oper;
- Test, interlock, synchrocheck, UTC time, and binary-time handling;
- direct normal and SBO normal execution;
- direct enhanced positive and negative termination;
- asynchronous SBOw application error;
- SBO enhanced sequence and missing-termination timeout;
- selection mutation cancellation;
- explicit and automatic selection timeout cleanup;
- association loss;
- concurrent clients on one object;
- exact descriptor discovery;
- generic MMS control-write blocking;
- InformationReport subscriber fan-out and association reset behavior.

## Required evidence before a production-readiness claim

1. DPC Open/Close on all four control models.
2. SPC On/Off.
3. INC/ISC and BSC implementation variants.
4. APC integer and floating variants.
5. Test-mode operation with independent verification of no process movement.
6. Interlock and synchrocheck rejection evidence.
7. Positive and negative CommandTermination evidence.
8. SBO expiry and explicit Cancel.
9. Association loss during an active sequence.
10. Competing-client ownership.
11. Process feedback correlation using the discovered status reference.
12. Long-duration repeated operation under an approved laboratory procedure.
13. Asset-owner and site-specific review for any operational deployment.

Keep product applications read-only when required live type, sequence, authority, or procedure evidence is missing.
