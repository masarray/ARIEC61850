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

Do not forget:

- `RptEna=true` is activation, not proof of report delivery;
- one receive pump must route confirmed responses and unsolicited reports concurrently;
- DataSet member order is semantic;
- every grouped Write result and effective post-write readback matters;
- `TrgOps` and `ReasonForInclusion` have a reserved leading significant bit;
- `ReasonForInclusion` payload `0x40` is data-change and `0x08` is integrity;
- BRCB `EntryID` is the buffered-record recovery identity; `SqNum` may restart;
- retained replay may burst, interleave with confirmed traffic, and contain duplicates without a resume anchor;
- production dynamic reporting remains subject to the stronger G2 qualification and `ProductionEligible` gates;
- raw external captures are not public fixtures; derive project-owned synthetic vectors instead.
