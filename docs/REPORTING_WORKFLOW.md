# MMS Reporting Workflow

IEC 61850 reporting should not be treated as a blind `RptEna=true` shortcut. The safe flow is discovery first, planning second, guarded enable third, and cleanup last.

## Recommended product flow

```text
Connect IED
→ Discover model
→ Discover DataSets
→ Discover RCBs
→ Select candidate RCB
→ Validate readiness
→ Enable reporting
→ Trigger GI when needed
→ Monitor reports
→ Export evidence
→ Disable and cleanup
```

## Why RCB/DataSet selection belongs in a wizard

RCB and DataSet selection is a setup decision, not a runtime action that operators should keep changing during monitoring. A reporting wizard should help the user make this once, validate it, then enter a stable monitoring workspace.

Runtime should focus on:

- active RCB identity;
- bound DataSet identity;
- DataSet member order;
- report count;
- sequence number and EntryID movement;
- GI status;
- buffer overflow evidence;
- inclusion bitstring and reason-for-inclusion diagnostics;
- raw MMS trace and exported evidence.

## Guardrails

Before enabling a report session, the UI/CLI should show:

- RCB reference;
- buffered/unbuffered type;
- current `RptEna` state;
- reservation / ownership evidence when available;
- DataSet reference and member count;
- ConfRev evidence;
- OptFlds and TrgOps evidence;
- whether the operation will write `DatSet`, `RptEna`, `GI`, or cleanup fields.

The final enable action should be explicit and should avoid changing an already-used RCB unless the user confirms the risk.

## Evidence outputs

Generated report evidence should go to ignored local folders such as:

```text
.artifacts/out/
evidence/
captures/
```

Do not commit runtime evidence, real IED captures, customer station names, relay serials, or live network details into the public repository.
