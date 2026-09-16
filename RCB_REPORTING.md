# RCB Reporting — Start Here

For any ARIEC61850 work involving URCB, BRCB, dynamic/static DataSets, `RptEna`, `InformationReport`, `EntryID`, reconnect/replay, or report bit-field semantics, read:

**[`docs/RCB_REPORTING_REFERENCE.md`](docs/RCB_REPORTING_REFERENCE.md)**

Then use the existing evidence/qualification contracts where applicable:

- [`docs/G2_EVIDENCE_QUALIFIED_DYNAMIC_REPORTING.md`](docs/G2_EVIDENCE_QUALIFIED_DYNAMIC_REPORTING.md)
- [`docs/G2_4_TRANSACTIONAL_RCB_FIELD_LEASE.md`](docs/G2_4_TRANSACTIONAL_RCB_FIELD_LEASE.md)
- [`docs/G2_6_PRODUCTION_DYNAMIC_CONSUMER.md`](docs/G2_6_PRODUCTION_DYNAMIC_CONSUMER.md)

Thirty-second contract:

```text
Static URCB  : Read -> Resv -> RptEna -> readback -> async reports
Static BRCB  : Read -> RptEna -> readback -> async reports
Dynamic URCB : Define+verify DataSet -> Read -> Resv -> configure -> RptEna last -> readback
Dynamic BRCB : Define+verify DataSet -> Read -> configure -> RptEna last -> readback
```

For an interactive client that should display current values immediately after Connect, the preferred bootstrap is:

```text
Connect / associate
  -> discover live RCBs and DataSets
  -> reconcile SCL ReportControl family to concrete live RCB instances
  -> select a safe concrete RCB from that family
  -> configure/reserve as required
  -> RptEna=true
  -> register persistent report routing
  -> one-shot GI=true when the live RCB explicitly exposes GI
  -> receive/map the initial DataSet report
  -> continue event-driven reporting
```

SCL-to-live RCB identity rules:

- SCL is declarative context; the live MMS directory is authoritative for concrete online RCB instances and ownership state.
- Never synthesize a runtime RCB name from an SCL base name. An indexed SCL control such as `Buffer` may reconcile to concrete live names such as `Buffer1`, `Buffer01`, or `Buffer001` only when those instances are actually observed live in the same domain, logical node, and report functional constraint.
- A non-indexed SCL `ReportControl` requires an exact live control-block name.
- Do not cross from an SCL family to an unrelated free RCB merely because it is available.
- If family identity is ambiguous or absent, fail closed and perform no report-control write.

Do not forget:

- `RptEna=true` is activation, not proof of report delivery;
- initial GI is a one-shot value bootstrap, not a periodic polling loop;
- register persistent report routing before issuing the bootstrap GI so a fast IED response cannot arrive before a monitor exists;
- do not issue a blind GI write when the live RCB attribute inventory has not established GI capability;
- one receive pump must route confirmed responses and unsolicited reports concurrently;
- DataSet member order is semantic;
- every grouped Write result and effective post-write readback matters;
- `TrgOps` and `ReasonForInclusion` have a reserved leading significant bit;
- `ReasonForInclusion` payload `0x40` is data-change and `0x08` is integrity;
- BRCB `EntryID` is the buffered-record recovery identity; `SqNum` may restart;
- retained replay may burst, interleave with confirmed traffic, and contain duplicates without a resume anchor;
- production dynamic reporting remains subject to the stronger G2 qualification and `ProductionEligible` gates;
- raw external captures are not public fixtures; derive project-owned synthetic vectors instead.
