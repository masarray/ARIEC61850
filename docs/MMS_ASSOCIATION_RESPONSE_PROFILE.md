# MMS Association Response Profile

`mms-association-response-profile` is a server-side transport milestone for the ARIEC61850 engine. It exercises a loopback TCP session with real TPKT/COTP frames, accepts an IEC 61850 MMS associate request payload, sends a deterministic ACSE AARE + MMS InitiateResponse profile, and verifies that the client-side probe can inspect the response.

This is not a full MMS server yet. It is the next protocol gate after the handshake listener profile and before confirmed MMS request dispatch.

## Run

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-association-response-profile --port 0 --output .\.artifacts\out\mms-association-response.md --json .\.artifacts\out\mms-association-response.json
```

`--port 0` uses an ephemeral loopback port and does not require administrator privileges.

## What it proves

- TCP loopback listener starts and accepts one client.
- Client sends TPKT-wrapped COTP Connection Request.
- Server sends COTP Connection Confirm.
- Client sends COTP Data containing an ACSE/MMS associate request profile.
- Server inspects the request as AARQ/MMS InitiateRequest-like payload.
- Server sends COTP Data containing ACSE AARE + MMS InitiateResponse marker.
- Client inspects the response and validates the association response gates.

## What it does not claim yet

- No full MMS confirmed-request dispatcher.
- No live GetNameList/Read response over MMS PDU yet.
- No conformance claim.
- No authentication or security layer.

## Response profiles

```powershell
--response-profile DeterministicInitiateResponse
--response-profile CompactInitiateResponse
```

The deterministic profile is the default because it carries a richer response payload and negotiated MMS limit metadata. The compact profile is a small smoke-test response for transport readiness.

## Next engine gate

The next milestone is to map the accepted association to a live read-only MMS request path:

```text
ACSE AARE + MMS InitiateResponse
→ confirmed request decode
→ GetNameList dispatch
→ Read dispatch
→ DataSet directory dispatch
→ read-only write rejection
```
