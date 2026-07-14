# IED Discovery Control Tester

## Purpose

The IED Discovery application exposes a guarded live-control workspace for a selected IEC 61850 control Data Object such as:

```text
LD0/CSWI1.Pos
```

The operator works with process intent (`OPEN` / `CLOSE`). The application does not ask the operator to choose `Oper`, `SBO`, or `SBOw`; the `AR.Iec61850.Control` service discovers `ctlModel` and applies the required sequence.

This is a laboratory and approved-commissioning tool. It is not a substitute for switching authority, site procedures, protection blocking plans, interlocking approval, independent indications, or asset-owner authorization.

## Operator workflow

1. Connect to an approved test IED with **Discover IED**.
2. In the live model tree, select the intended control Data Object, normally `CSWI.Pos`.
3. Click **Control**.
4. Verify the displayed:
   - control-object reference;
   - discovered control model;
   - status reference;
   - current process state;
   - protocol readiness and limitations.
5. Configure advanced parameters when required:
   - Interlock check;
   - Synchrocheck;
   - Test command;
   - Originator category;
   - Orig ID;
   - command timeout.
6. Verify switching authority, isolation, blocking, approved procedure, and independent indications.
7. Stage **OPEN** or **CLOSE**.
8. Review the per-command confirmation and target.
9. Dispatch only after the approved test conditions are confirmed by the responsible person.
10. Review immediate MMS response, command termination, application errors, and final process feedback as separate evidence.

The command matching the current process state is normally suppressed to reduce accidental duplicate operation. Test mode may intentionally target the current state, but the operator remains responsible for the approved test procedure.

## Automatic sequence selection

| Discovered `ctlModel` | Sequence | Completion boundary |
|---|---|---|
| Direct normal | `Oper` | confirmed MMS result, followed by feedback review |
| SBO normal | read/select `SBO`, then `Oper` | confirmed MMS result, followed by feedback review |
| Direct enhanced | `Oper`, then CommandTermination | positive or negative termination, followed by feedback review |
| SBO enhanced | `SBOw`, then `Oper`, then CommandTermination | positive or negative termination, followed by feedback review |

`ctlNum` and `T` are generated and retained consistently for the sequence. The operator does not enter them manually.

## Status interpretation

For a DPC `Pos.stVal`, the tester displays:

| DPC value | Display |
|---|---|
| intermediate-state | `Intermediate` |
| off | `OPEN` |
| on | `CLOSED` |
| bad-state | `Bad / invalid` |

The Functional Constraint used for feedback is discovered from the live MMS directory when possible. It is not hard-coded in the WPF application.

A displayed state is protocol evidence from the selected reference. It is not, by itself, independent proof of primary-equipment position.

## Check and origin fields

The advanced fields map to the typed request:

- **Interlock check** → IEC 61850 `Check` interlock bit.
- **Synchrocheck** → IEC 61850 `Check` synchrocheck bit.
- **Test command** → `Test=true`.
- **Originator** → `origin.orCat`.
- **Orig ID** → `origin.orIdent`, validated against the live field size.
- **Command timeout** → client wait limit for the completion boundary.

The IED remains authoritative for its control logic. Requesting a check does not simulate, bypass, or independently approve the IED's interlocking or synchronism logic.

## Evidence and failure handling

The tester preserves:

- discovered control model and live type signatures;
- sequence steps;
- immediate MMS acceptance or rejection;
- positive or negative CommandTermination;
- `ControlError`;
- `AddCause`;
- `LastApplError` details;
- generated `ctlNum` and timestamp `T`;
- request and response evidence;
- final status readback.

For SBO objects, cancellation before accepted `Oper` performs best-effort `Cancel` and releases local ownership. Association loss, timeout, caller cancellation, service rejection, negative termination, and feedback mismatch remain distinct outcomes.

Best-effort cleanup is not proof that the remote IED completed cleanup. Review the returned evidence and remote state.

## Recommended first laboratory validation

Use an isolated test IED or an approved test mode:

1. Read status only.
2. Confirm the control object, `ctlModel`, status reference, and live type signatures.
3. Exercise Test mode under an approved procedure and independently verify expected process behavior.
4. Validate Direct/SBO and normal/enhanced sequences separately.
5. Capture positive and negative termination paths.
6. Verify interlock and synchrocheck rejection behavior.
7. Verify SBO timeout, explicit Cancel, association loss, and competing-client behavior.
8. Compare application evidence with an independently observed protocol trace and physical or secondary indication.
9. Repeat non-Test operation only after approval and risk controls are in place.

Do not infer that a Test command cannot move equipment unless the specific IED configuration, wiring, procedure, and independent observation establish that behavior.

## Build and focused tests

```powershell
dotnet restore .\ARIEC61850.sln
dotnet build .\ARIEC61850.sln -c Release
dotnet test .\tests\AR.Iec61850.Tests\AR.Iec61850.Tests.csproj `
  -c Release `
  --no-build `
  --filter "FullyQualifiedName~SmartControlStackTests"
```

Run the WPF tester:

```powershell
dotnet run --project .\apps\AR.Iec61850.IedDiscovery\AR.Iec61850.IedDiscovery.csproj -c Release
```

## Current evidence boundary

The source provides typed sequencing and automated unit coverage. A limited laboratory command path has been recorded. Each IED family, model, firmware, CDC variant, and control model still requires evidence for normal and enhanced completion, application-error cases, feedback mapping, selection expiry, association loss, competing-client behavior, and long-duration reliability before a production-readiness claim.
