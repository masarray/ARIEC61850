# MMS Read-Only Server Loopback Alpha Profile

The MMS read-only server loopback alpha profile is the first unified server-side readiness gate for ARIEC61850. It combines the virtual IED model, TPKT/COTP transport exchange, ACSE association response profile, and native MMS BER confirmed-request dispatch into one evidence artifact.

The profile is intentionally read-only. It validates the safe server path before any live write/control behavior is introduced.

## What it validates

- Virtual IED model readiness.
- TPKT exchange.
- COTP connection confirmation.
- ACSE associate request observation.
- ACSE AARE + MMS InitiateResponse profile acceptance.
- Native MMS BER confirmed-request decoding.
- Native MMS BER confirmed-response encoding.
- Logical device directory dispatch.
- Logical node / named variable directory dispatch.
- DataSet directory dispatch.
- Point read dispatch.
- Write rejection guard.

## CLI

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-readonly-loopback-profile --port 0 --output .\.artifacts\out\mms-readonly-loopback.md --json .\.artifacts\out\mms-readonly-loopback.json
```

`--port 0` uses an ephemeral loopback port, so the profile can be tested without administrator rights and without binding to TCP/102.

## Scope

This milestone is not a complete live MMS server. It is a loopback alpha profile that proves the server-side lifecycle:

```text
model
→ association
→ native BER confirmed-request
→ read-only dispatch
→ native BER confirmed-response
→ write guard
```

The next server milestone should move from loopback evidence into a reusable server facade with explicit start/stop lifecycle, cancellation, and service registration points.
