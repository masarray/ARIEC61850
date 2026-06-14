# MMS Read-Only Server Profile

The MMS read-only server profile is the first server-side milestone for the ARIEC61850 engine. It converts the simulator model into a deterministic virtual IED model that can answer offline service-style probes without opening a TCP listener yet.

This milestone is intentionally read-only. It is designed to validate the server data model, DataSet exposure, RCB exposure, read behavior, and write-guard behavior before adding a live MMS TCP/ACSE listener.

## What it proves

- Logical-device directory can be derived from the simulator profile.
- Logical-node directory can be derived per logical device.
- Readable points expose value, quality, functional constraint, and timestamp.
- DataSets resolve to member values.
- Report-control blocks expose DataSet, mode, ConfRev, trigger options, and optional fields.
- Write requests are rejected by design.
- Markdown and JSON evidence can be generated for regression and product-app integration.

## Offline test

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-server-readonly-profile --steps 5 --output .\.artifacts\out\mms-server-readonly.md --json .\.artifacts\out\mms-server-readonly.json
```

Optional probes:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-server-readonly-profile --read IED1LD0/XCBR1.Pos.stVal --dataset IED1LD0/LLN0.dsStatus --steps 10
```

Use `/` in object references when possible:

```text
IED1LD0/XCBR1.Pos.stVal
IED1LD0/LLN0.dsStatus
```

## Current scope

Included in this alpha:

- offline server model profile;
- high-level service request/response handler;
- logical-device directory;
- logical-node directory;
- DataSet directory;
- RCB directory;
- point read;
- DataSet read;
- variable-attribute summary;
- write rejection;
- self-test evidence.

Not included yet:

- TCP port 102 listener;
- TPKT/COTP/ACSE server association;
- MMS Initiate response;
- binary MMS request/response serving;
- online report publication;
- control model;
- file/log/setting-group services.

Those are next server-side milestones.
