# IED Discovery Smart Control Tester

## Purpose

The IED Discovery application now exposes a guarded live-control workspace for a selected IEC 61850 control Data Object such as:

```text
LD0/CSWI1.Pos
```

The operator works with process intent (`OPEN` / `CLOSE`). The application does not ask the operator to choose `Oper`, `SBO`, or `SBOw`; the native `AR.Iec61850.Control` service discovers `ctlModel` and executes the required sequence.

This is a commissioning/laboratory tester. It is not a substitute for site switching procedures, protection blocking plans, or formal interlocking approval.

## Operator workflow

1. Connect to the IED with **Discover IED**.
2. In the live model tree, select the controllable Data Object, normally `CSWI.Pos`.
3. Click **Control**.
4. Confirm the tester shows:
   - the expected control-object reference;
   - the automatically detected control model;
   - a readable status reference;
   - the current process state (`OPEN`, `CLOSED`, intermediate, or bad).
5. Open **Advanced control parameters** only when needed:
   - Interlock check;
   - Synchrocheck;
   - Test command;
   - Originator category;
   - Orig ID;
   - command timeout.
6. Confirm the IED and test circuit are safe, then select **Enable live command**.
7. Press **OPEN** or **CLOSE** and review the confirmation dialog.
8. Verify the final result in both:
   - the command evidence table;
   - the refreshed live status value.

The command matching the current process state is disabled to reduce accidental duplicate operation. It becomes available when `Test=true`, because a test command may intentionally target the current state without requiring process movement.

## Automatic sequence selection

| Discovered `ctlModel` | Tester sequence | Success boundary |
|---|---|---|
| Direct normal | `Oper` | confirmed MMS acceptance, followed by status feedback check |
| SBO normal | read/select `SBO`, then `Oper` | confirmed MMS acceptance, followed by status feedback check |
| Direct enhanced | `Oper`, then wait for CommandTermination | positive termination, followed by status feedback check |
| SBO enhanced | `SBOw`, then `Oper`, then wait for CommandTermination | positive termination, followed by status feedback check |

`ctlNum` and `T` are generated automatically and retained consistently for the sequence. The operator does not enter them manually.

## Status interpretation

For a DPC `Pos.stVal`, the tester displays:

| DPC value | Display |
|---|---|
| intermediate-state | `Intermediate` |
| off | `OPEN` |
| on | `CLOSED` |
| bad-state | `Bad / invalid` |

The functional constraint used for status read is discovered from the live MMS directory (`ST` for normal position state, or `MX` for supported measured feedback variants). It is not hard-coded in the WPF application.

## Check and origin fields

The advanced fields map directly to the native request:

- **Interlock check** → IEC 61850 `Check` interlock bit.
- **Synchrocheck** → IEC 61850 `Check` synchrocheck bit.
- **Test command** → `Test=true`.
- **Originator** → `origin.orCat`.
- **Orig ID** → `origin.orIdent`, validated against the live field size.
- **Command timeout** → client wait limit for the completion boundary.

The IED remains authoritative. Enabling a check requests the IED to evaluate it; the tester does not simulate or bypass the IED's control logic.

## Evidence and failure handling

The tester preserves:

- discovered control model and live type signatures;
- sequence steps;
- immediate MMS acceptance/rejection;
- positive or negative CommandTermination;
- `ControlError`;
- `AddCause`;
- `LastApplError` details;
- generated `ctlNum` and timestamp `T`;
- request and response/termination hex previews;
- final process-status read.

For SBO objects, cancellation before an accepted `Oper` performs best-effort `Cancel` and releases local sequence ownership. Association loss, timeout, caller cancellation, service rejection, and negative command termination remain distinct result states.

## Recommended first live test

Use an isolated test IED or test mode and start with:

1. Read status only.
2. `Test=true`, Interlock enabled, Synchrocheck as required by the IED.
3. OPEN or CLOSE toward the current state to validate encoding without requiring movement.
4. Repeat with `Test=false` only after origin, checks, and status mapping are confirmed.
5. Capture Wireshark or IEDScout evidence and compare the displayed sequence.

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

## Current proof boundary

The source provides the typed workflow and automated unit coverage. Each IED vendor/model/firmware still requires live evidence for Direct/SBO, normal/enhanced completion, negative AddCause cases, status feedback, selection expiry, and association-loss behavior before a production-readiness claim.
