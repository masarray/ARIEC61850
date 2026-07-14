# SCL Engineering Profile

The SCL engineering profile converts ICD, CID, IID, and SCD files into a typed expected-model view for laboratory and commissioning review. It is application-neutral so product applications can consume the same engine result without duplicating parser or validation logic.

## What it extracts

- IED inventory and access points
- server, logical-device, and logical-node structure
- DataSet identity, member order, and counts
- expected report sessions
- expected GOOSE streams
- expected Sampled Values streams
- subscriber `Inputs/ExtRef` mapping
- declared service capabilities from `Services`
- static engineering findings with source and context

## CLI

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- scl-engineering-profile .\samples\scl\minimal-station.scd --output .\.artifacts\out\scl-profile.md --json .\.artifacts\out\scl-profile.json
```

The command is offline and read-only. It does not require an IED or network adapter.

## Why this matters

Reporting, GOOSE, Sampled Values, simulation, and evidence workflows need a common expected model. The profile provides that model before live MMS discovery or process-bus observation is compared.

```text
SCL expected model
→ live MMS discovered model
→ observed process-bus traffic
→ report and process-bus runtime evidence
→ explicit match, partial, mismatch, or missing findings
```

## Claim boundary

The profile produces engineering findings intended for laboratory and commissioning review. It does not prove that an SCL file matches the installed system, that every live device exposes the same model, or that the project is formally conformant.

Findings such as missing publishers, incomplete communication addresses, empty report DataSets, unresolved subscriber references, duplicate APPIDs, or model mismatch require confirmation against approved project documentation and live evidence.
