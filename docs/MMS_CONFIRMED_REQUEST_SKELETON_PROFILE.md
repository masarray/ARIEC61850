# MMS Confirmed Request Skeleton Profile

The MMS confirmed-request skeleton profile is an engine milestone that exercises the first live read-only request/response path after TCP, TPKT, COTP, ACSE AARE, and MMS InitiateResponse profile exchange.

Scope:

- TCP loopback listener
- TPKT frame exchange
- COTP Connection Request / Connection Confirm
- COTP Data TPDU carrying an ACSE AARQ association profile
- COTP Data TPDU carrying an ACSE AARE + MMS InitiateResponse profile
- COTP Data TPDU carrying clean-room skeleton confirmed-request envelopes
- Dispatch to the read-only virtual server model
- COTP Data TPDU carrying clean-room skeleton confirmed-response envelopes
- Markdown and JSON evidence

This milestone intentionally does **not** claim full MMS ConfirmedRequest BER decoding yet. The request envelope is a deterministic clean-room harness used to validate server lifecycle, dispatch semantics, read-only guard behavior, and evidence generation before the full MMS PDU decoder is attached to the listener.

## CLI

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-confirmed-request-skeleton-profile --port 0 --output .\.artifacts\out\mms-confirmed-request-skeleton.md --json .\.artifacts\out\mms-confirmed-request-skeleton.json
```

`--port 0` uses an ephemeral loopback port and does not require administrator privileges.

## Evidence gates

The profile is considered ready when all of these are true:

- TPKT exchange is verified.
- COTP connection is confirmed.
- Client association request is observed.
- Server association response is sent.
- Client association response is accepted.
- Confirmed request skeleton is observed.
- Confirmed response skeleton is accepted.
- Read-only server dispatch succeeds for directory/read/DataSet requests.
- Write guard is verified.

## Why this matters

This is the bridge between association-only listener milestones and a real read-only MMS server. After this foundation is stable, the next step is to replace the skeleton envelope with actual MMS ConfirmedRequest BER decoding and ConfirmedResponse encoding for services such as GetNameList, Read, and DataSet directory operations.
