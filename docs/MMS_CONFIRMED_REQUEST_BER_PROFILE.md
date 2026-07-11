# MMS Confirmed Request BER Dispatch Profile

The MMS confirmed-request BER dispatch profile is an engine milestone for the read-only virtual IED server path.

It validates this loopback path:

```text
TCP loopback
→ TPKT
→ COTP CR/CC
→ ACSE AARQ
→ ACSE AARE + MMS InitiateResponse profile
→ COTP Data carrying native MMS BER ConfirmedRequest
→ read-only service dispatch
→ COTP Data carrying native MMS BER ConfirmedResponse
→ client-side response decoder
```

This is still not a complete MMS server. The milestone proves the first native BER confirmed-request dispatch path for safe read-only services.

## Run

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-confirmed-request-ber-profile --port 0 --output .\.artifacts\out\mms-confirmed-request-ber.md --json .\.artifacts\out\mms-confirmed-request-ber.json
```

`--port 0` uses an ephemeral loopback port and does not require administrator privilege.

## Covered services

The default probe exercises:

- `GetNameList` for logical device directory
- `GetNameList` for logical node / named variable directory
- `GetNameList` for DataSet / named variable list directory
- paged `GetNameList` responses with `continueAfter` and `moreFollows`
- logical-node and functional-constraint hierarchy entries in named-variable browse results
- `Read` for one simulated point
- `GetNamedVariableListAttributes` for one DataSet
- `Write` rejection to verify the read-only guard

## Evidence gates

The profile reports whether the following gates passed:

- TPKT exchange verified
- COTP connection confirmed
- ACSE associate request observed
- ACSE associate response accepted
- native MMS BER request decoded
- native MMS BER response encoded
- client-side native response decoded
- directory dispatch verified
- read dispatch verified
- DataSet directory dispatch verified
- write guard verified

## Scope

This profile intentionally keeps the server read-only and deterministic. It does not yet implement a production MMS server listener, segmentation, full service coverage, access control, control model, file service, log service, or setting group service.
