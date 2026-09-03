# G2.6 Production Dynamic Reporting Consumer

## Status

Implemented and unit-tested on the `g2.6-production-dynamic-consumer` branch. This document describes the production-planning contract only; it is not a claim of new live-IED interoperability.

## Purpose

P6.2-B deliberately quarantines automatic full dynamic DataSet activation because advertised MMS capability and a successful single-member NamedVariableList probe were not sufficient evidence for safe production mutation.

G2.6 adds the missing production consumer for the existing persisted qualification profile. The capability-aware hybrid planner can now consider dynamic reporting only when an application supplies a `MmsDynamicReportProductionPlanningContext` containing the persisted profile and the current IED identity.

## Authorization gate

Dynamic production planning requires all of the following:

1. dynamic BRCB or URCB intent is enabled in planner options;
2. the current association passes the existing dynamic-report capability evaluator;
3. `MmsDynamicReportQualificationProfilePolicy.CanUseForProductionPlanning(...)` accepts the profile;
4. the profile is identity-compatible and exactly `ProductionEligible`;
5. the activation proof and InformationReport proof agree on RCB, DataSet, and exact member sequence;
6. the InformationReport member set is an ordered subset of the accepted qualified envelope;
7. the production member evidence is non-empty and contains no duplicate normalized MMS references.

A profile at `InformationReportProven` or any earlier state remains quarantined.

## First production-consumer scope

The initial consumer is intentionally narrower than the theoretical qualified envelope:

- static BRCB/URCB coverage is always planned first;
- automatic dynamic planning is restricted to the exact RCB that produced the proven InformationReport;
- only exact members from that proven InformationReport are exposed to the production dynamic planner;
- automatic production dynamic scale-out is limited to one dynamic group;
- the per-report member ceiling is clamped to the proven InformationReport member count;
- unproven or unrelated requested members remain on bounded MMS polling;
- another free RCB cannot silently substitute for the proven RCB.

This deliberately avoids generalizing a successful NVL envelope into report authority for members or RCBs that have not produced the proven report.

## Fresh live evidence remains mandatory

`ProductionEligible` is permission to consider the dynamic path, not permission to perform a blind write. Normal live planning still requires exact fresh RCB availability. If the proven RCB is unavailable, occupied, not explicitly free, or otherwise fails the existing dynamic-slot rules, the planner emits no dynamic segment and leaves the affected signals on polling.

## Post-plan invariant

Persisted profile material is treated as untrusted input. After the generic hybrid planner returns, the capability-aware wrapper verifies that any dynamic segment:

- uses no more than one dynamic RCB;
- uses the exact proven RCB;
- contains resolved dynamic points;
- contains only an ordered subset of the exact proven InformationReport member set.

If this invariant fails, the dynamic plan is discarded and planning is rebuilt with the frozen static-to-polling behavior.

## Compatibility

The existing `MmsCapabilityAwareHybridReportAcquisitionPlanner.Build(...)` call remains source-compatible. The production context is an optional final argument. Existing callers that do not provide it retain P6.2-B quarantine behavior.

## Deterministic validation

`MmsG26ProductionDynamicConsumerTests` covers:

- `InformationReportProven` remains quarantined;
- an identity-compatible `ProductionEligible` profile can authorize the exact proven URCB/member set;
- an unproven requested member remains on polling;
- identity/fingerprint mismatch fails closed;
- a different free RCB cannot substitute for the proven RCB;
- tampered persisted member evidence is rejected.

CI command set:

```powershell
dotnet restore .\ARIEC61850.sln
dotnet build .\ARIEC61850.sln -c Release --no-restore
dotnet test .\ARIEC61850.sln -c Release --no-build
.\scripts\verify-source-clean.ps1
```

The first successful branch validation ran 703 tests with 703 passed, zero build warnings, and zero build errors.

## What remains unproven

This engine patch does not itself make any IED `ProductionEligible` and does not establish live field behavior for a new device. Applications must still complete the physical G2.6 acceptance gates before persisting `ProductionEligible`, then supply that profile and the matching current IED identity to this consumer.

The next lowest-risk application step is to wire the typed production context into ARSAS while keeping current non-qualified IEDs on the existing static-report/polling behavior.