# SCL Engineering Profile

The SCL engineering profile is a static engine milestone used to turn ICD/CID/IID/SCD files into a testable engineering evidence model.  It is intentionally application-neutral: product applications can consume the same profile later without duplicating parser or validation logic.

## What it extracts

- IED inventory and access points
- Server / logical-device / logical-node structure
- DataSet count and member counts
- Expected report sessions
- Expected GOOSE streams
- Expected Sampled Values streams
- Subscriber `Inputs/ExtRef` mapping
- Declared service capabilities from `Services`
- Static engineering findings

## CLI

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- scl-engineering-profile .\samples\scl\minimal-station.scd --output .\.artifacts\out\scl-profile.md --json .\.artifacts\out\scl-profile.json
```

The command is offline and read-only. It does not require an IED or network adapter.

## Why this matters

Report, GOOSE, SV, simulator, and evidence engines need a common expected model. The profile provides that expected model before live capture or MMS connection is used.

The next engine phases should compare:

```text
SCL expected model
→ MMS discovered model
→ process-bus observed traffic
→ report/GOOSE/SV runtime evidence
```

This enables field-grade findings such as missing publishers, incomplete communication addresses, empty report DataSets, unresolved subscriber references, duplicate APPIDs, and model mismatch between engineering file and live device.
