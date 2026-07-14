# MMS Reporting Workflow

IEC 61850 reporting should not be treated as a blind `RptEna=true` shortcut. The guarded flow is discovery first, planning second, explicit confirmation third, monitoring fourth, and cleanup last.

## Recommended workflow

```text
Connect IED
→ discover model
→ discover DataSets
→ discover RCBs
→ refresh live RCB state
→ classify ownership and readiness
→ select a candidate RCB
→ validate DataSet identity and member order
→ review the typed write plan
→ confirm required writes
→ enable reporting
→ trigger GI when approved
→ monitor reports
→ export evidence
→ stop and clean up
```

## Why RCB and DataSet selection belongs in setup

RCB and DataSet selection is a setup decision, not a runtime action that operators should repeatedly change during monitoring. A report setup workflow should validate the choice once and then enter a stable monitoring view.

Runtime should focus on:

- active RCB identity;
- bound DataSet identity;
- member count and order;
- ownership or reservation evidence;
- report count;
- sequence number and EntryID movement;
- GI state;
- buffer overflow evidence;
- inclusion bitstring and reason-for-inclusion;
- typed values, quality, timestamps, and source;
- protocol evidence and cleanup state.

## Guardrails

Before enabling a report session, show:

- RCB reference;
- buffered or unbuffered type;
- current `RptEna` state;
- reservation and ownership evidence when available;
- DataSet reference and member count;
- `ConfRev`, `OptFlds`, and `TrgOps` evidence;
- every field the operation may write;
- whether a temporary DataSet will be created;
- expected cleanup behavior and limitations.

Treat an RCB enabled or reserved by another client as occupied. Do not silently overwrite its configuration.

## Persistent monitoring

An interactive monitor may keep the selected RCB enabled until the user chooses **Stop RCB**, closes the IED, the session faults, or the application exits. Stop should attempt to:

1. disable the RCB;
2. release reservation touched by the session;
3. remove a temporary dynamic DataSet created by the application;
4. preserve cleanup evidence and any failure.

Cleanup is best effort. A local success state does not prove that the remote IED completed every requested cleanup operation.

## Evidence outputs

Generated report evidence belongs in ignored local folders such as:

```text
.artifacts/out/
evidence/
captures/
```

Do not commit runtime evidence, real IED captures, customer station names, relay serials, credentials, or live network details into the public repository.

## Claim boundary

Guarded planning reduces accidental configuration changes; it does not prove report interoperability, cybersecurity, operational safety, or complete BRCB recovery behavior. Validate each IED family and use case under an approved procedure.
