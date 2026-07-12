# ARIEC61850 Smart Control Stack — Implementation Report

Date: 2026-07-12  
Scope: native IEC 61850 client-side control service in `AR.Iec61850`

## Executive result

The repository now contains a native, stateful IEC 61850 control-object stack under `AR.Iec61850.Control`. Applications address a controllable Data Object such as `LD0/CSWI1.Pos` and submit intent such as Open or Close. The engine discovers the live control model and MMS type, builds the exact command value, owns the complete Direct/SBO sequence, and reports service acceptance separately from final command termination.

This is a clean-room C# implementation informed by IEC 61850 control behavior and established commissioning workflows. No third-party protocol-stack source code is embedded or linked.

## Implemented architecture

### 1. Live control descriptor

`Iec61850ControlService.OpenAsync`:

- validates a Data Object root and rejects service leaves;
- reads `CF/ctlModel`;
- retrieves exact live `Oper`, `SBOw`, and `Cancel` variable specifications;
- locates named `ctlVal` instead of guessing structure positions;
- validates Oper/SBOw type compatibility;
- discovers SBO/operate timeouts and a likely status feedback reference;
- refuses to open an unsafe or incomplete control descriptor.

### 2. Exact value binding

The binder supports:

- SPC Boolean On/Off;
- DPC two-bit Open/Close;
- INC/ISC signed and unsigned values, including Raise/Lower helpers;
- BSC `ValWithTrans`-style structures;
- APC integer and floating `AnalogueValue` variants;
- explicit vendor-specific raw MMS values with live kind, structure, bit-width, and length validation.

### 3. Native sequence executor

The executor supports all four control models:

| Control model | Executed sequence | Success boundary |
|---|---|---|
| Direct normal | `Oper` | confirmed MMS service acceptance |
| SBO normal | `SBO` read → `Oper` | confirmed MMS service acceptance |
| Direct enhanced | `Oper` → CommandTermination | positive termination |
| SBO enhanced | `SBOw` → `Oper` → CommandTermination | positive termination |

One immutable sequence retains `ctlVal`, origin, `ctlNum`, `T`, Test, Check, and optional `operTm`. Mutation after selection is rejected and the stale selection is cancelled.

### 4. Ownership and failure safety

- association-scoped object ownership;
- per-session operation serialization against double-click/race conditions;
- per-association/object `ctlNum` generation;
- automatic SBO lease timeout;
- best-effort `Cancel` on timeout, mutation, disposal, and safe cleanup paths;
- local ownership release even after association loss;
- distinct states for accepted, positive/negative termination, timeout, association loss, caller cancellation, rejection, and unsupported operation;
- command wait deadline includes time-activated delay but does not accidentally wait twice after the MMS write.

### 5. Receive-path integration

InformationReport subscriptions fan out from the existing single MMS receive router. Command termination no longer steals reports from legacy consumers and does not create a competing socket receive loop.

The decoder handles:

- exact positive `CO/Oper` CommandTermination;
- LastApplError structures;
- embedded `ctlObj` matching when the report variable name is generic;
- control error and AddCause values, including interlock, synchrocheck, object selection, access authority, inconsistent parameters, and locked-by-other-client;
- request/response hexadecimal evidence.

### 6. Hard safety boundary

Public generic MMS writes to `Oper`, `SBOw`, and `Cancel` are blocked. They can only be issued through the native control service.

## Main source additions

- `src/AR.Iec61850/Control/Iec61850ControlModels.cs`
- `src/AR.Iec61850/Control/Iec61850ControlObjectReferences.cs`
- `src/AR.Iec61850/Control/Iec61850ControlTransport.cs`
- `src/AR.Iec61850/Control/Iec61850ControlValueBinder.cs`
- `src/AR.Iec61850/Control/Iec61850ControlStructureBuilder.cs`
- `src/AR.Iec61850/Control/Iec61850CommandTerminationDecoder.cs`
- `src/AR.Iec61850/Control/Iec61850ControlService.cs`
- `src/AR.Iec61850/Control/Iec61850ControlObjectSession.cs`
- `src/AR.Iec61850/Mms/MmsInformationReportSubscription.cs`

MMS session/router/pump/report decoding and documentation were updated to support the stack.

## Test matrix added

`SmartControlStackTests` contains 32 test methods covering:

- control-root validation;
- exact FC references;
- DPC/SPC/INC/ISC/BSC/APC binding;
- rejection of raw values with wrong live width;
- immutable `ctlNum` and `T`;
- Test and Check bits;
- UTC time and six-byte MMS BinaryTime epoch;
- Direct normal and SBO normal;
- Direct enhanced positive and negative termination;
- SBO enhanced ordering and missing-termination timeout;
- asynchronous SBOw LastApplError;
- embedded control-object LastApplError matching;
- mutation cancellation;
- wrong SBO reference rejection;
- explicit and automatic SBO timeout cleanup;
- explicit Cancel;
- association loss;
- local competing-client serialization;
- descriptor readiness;
- generic MMS control-write blocking.

Receive-router tests also cover subscriber fan-out and association-reset fault propagation.

## Validation completed in this environment

- 309 C# files parsed using the C# tree-sitter grammar: no syntax-error nodes.
- 25 project/XML/XAML files parsed successfully.
- No `bin`, `obj`, `.artifacts`, or `.vs` directories in the package.
- No TODO/FIXME/NotImplemented placeholders in the new control source/tests.
- Patch whitespace check produced no reported whitespace errors.
- ZIP integrity check is performed during packaging.

## Validation not completed here

The environment does not contain the .NET SDK or a TCP/102 control-capable simulator/IED. Therefore the following remain release gates:

1. `dotnet restore`, Release build, and unit-test execution on Windows/.NET 8.
2. Simulator captures for all four control models.
3. Multi-vendor IED testing for DPC, SPC, INC/ISC, BSC, and APC variants.
4. Positive and negative CommandTermination evidence.
5. Test-mode, interlock, synchrocheck, SBO timeout, Cancel, association-loss, and external competing-client evidence.
6. Process feedback correlation using `Descriptor.StatusReference`.

Until those gates pass, this should be treated as a strong native control-stack foundation rather than a formal conformance claim.

## Recommended ARIED integration rule

Enable **Send Command** only when:

- the application is online;
- the selected row is a valid control Data Object root;
- `control.Descriptor.IsOperationallyReady` is true;
- the exact live value can be represented by a supported typed intent;
- the operator has reviewed Test, interlock-check, synchrocheck, origin, and target value;
- the environment is explicitly placed in commissioning/control mode.

For enhanced security, never present MMS write acceptance as command success. Show a pending state until positive or negative CommandTermination is received.
