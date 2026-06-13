# IEC 61850 Report Evidence

- Generated: 2026-06-12 21:44:45.983 UTC
- Target: 192.16.1.157:102
- Mode: mms-report-dynamic-live
- Plan: Report DynamicDataSet plan: status=ReadyRequiresWrite, rcb=OCR7SR12MEAS/LLN0.BR.brcbA01, dataset=OCR7SR12MEAS/LLN0.AR_DYN_DS01, members=2, dynamicPoints=2
- Result: PASS - Dynamic report guarded session complete: writes=7, reports=1.
- Verification: PASS_WITH_WARNING - verification=PASS_WITH_WARNING, pass=12, warnings=1, failures=0, rcbSnapshots=4, dataSetSnapshots=2
- Diagnostics: diagnostics=PASS_WITH_WARNING, reports=1, values=2, mappedFailures=0, partialMappings=0, pollReads=0/0, writeFailures=0, seqGaps=0, seqRegressions=0, entryIdGaps=0, entryIdRegressions=0, duplicates=0, bufOvfl=true
- Diagnostic status: PASS_WITH_WARNING
- EntryID: 0000000000000006 -> 0000000000000006

## Counts

| Metric | Value |
| --- | ---: |
| Reports | 1 |
| Report values | 2 |
| Mapping failures | 0 |
| Partial mappings | 0 |
| Poll reads OK | 0/0 |
| Write failures | 0 |
| Duplicate report keys | 0 |
| Sequence gaps | 0 |
| Sequence regressions | 0 |
| EntryID gaps | 0 |
| EntryID regressions | 0 |
| Buffer overflow observed | true |

## Diagnostic Warnings

- BRCB buffer-overflow flag was observed. Treat the session as usable evidence with a warning; check EntryID continuity and relay buffered-report history.

## Verification

- Status: PASS_WITH_WARNING
- Summary: verification=PASS_WITH_WARNING, pass=12, warnings=1, failures=0, rcbSnapshots=4, dataSetSnapshots=2

| Severity | Stage | Target | Expected | Observed | Message |
| --- | --- | --- | --- | --- | --- |
| Pass | before | OCR7SR12MEAS/LLN0.BR.brcbA01.RptEna | false | false | RptEna readback verified. |
| Pass | before | OCR7SR12MEAS/LLN0.BR.brcbA01.DatSet | empty | - | RCB.DatSet is empty/restored. |
| Pass | before | OCR7SR12MEAS/LLN0.BR.brcbA01.reservation | not active | Resv=- ResvTms=0 | RCB reservation readback verified as inactive. |
| Pass | after-create | OCR7SR12MEAS/LLN0.AR_DYN_DS01 | 2 member(s) in requested order | 2 member(s) | DataSet directory readback verified. |
| Pass | after-bind | OCR7SR12MEAS/LLN0.BR.brcbA01.RptEna | false | false | RptEna readback verified. |
| Pass | after-bind | OCR7SR12MEAS/LLN0.BR.brcbA01.DatSet | OCR7SR12MEAS/LLN0.AR_DYN_DS01 | OCR7SR12MEAS/LLN0.AR_DYN_DS01 | RCB.DatSet readback verified. |
| Pass | after-enable | OCR7SR12MEAS/LLN0.BR.brcbA01.RptEna | true | true | RptEna readback verified. |
| Pass | after-enable | OCR7SR12MEAS/LLN0.BR.brcbA01.DatSet | OCR7SR12MEAS/LLN0.AR_DYN_DS01 | OCR7SR12MEAS/LLN0.AR_DYN_DS01 | RCB.DatSet readback verified. |
| Pass | after-gi | InformationReport | at least 1 report | 1 | InformationReport received. |
| Pass | after-cleanup | OCR7SR12MEAS/LLN0.BR.brcbA01.RptEna | false | false | RptEna readback verified. |
| Pass | after-cleanup | OCR7SR12MEAS/LLN0.BR.brcbA01.DatSet | empty | - | RCB.DatSet is empty/restored. |
| Warning | after-cleanup | OCR7SR12MEAS/LLN0.BR.brcbA01.reservation | not active or lease-only | Resv=- ResvTms=42 | BRCB ResvTms lease timer is still visible while RptEna=false. Treat as relay ownership lease/timeout behavior, not cleanup failure. |
| Pass | after-delete | OCR7SR12MEAS/LLN0.AR_DYN_DS01 | not readable after delete | MMS Confirmed-Error PDU during DataSet directory: A2 0B 80 02 0A A8 A2 05 A0 03 87 01 02 | Dynamic DataSet delete verified by readback. |

### RCB Snapshots

- before: OCR7SR12MEAS/LLN0.BR.brcbA01 RptEna=false DatSet=- Resv=- ResvTms=0 ConfRev=11
- after-bind: OCR7SR12MEAS/LLN0.BR.brcbA01 RptEna=false DatSet=OCR7SR12MEAS/LLN0.AR_DYN_DS01 Resv=- ResvTms=0 ConfRev=12
- after-enable: OCR7SR12MEAS/LLN0.BR.brcbA01 RptEna=true DatSet=OCR7SR12MEAS/LLN0.AR_DYN_DS01 Resv=- ResvTms=42 ConfRev=12
- after-cleanup: OCR7SR12MEAS/LLN0.BR.brcbA01 RptEna=false DatSet=- Resv=- ResvTms=42 ConfRev=13

### DataSet Snapshots

- after-create: OCR7SR12MEAS/LLN0.AR_DYN_DS01 exists members=2 deletable=true
- after-delete: OCR7SR12MEAS/LLN0.AR_DYN_DS01 not readable/deleted: MMS Confirmed-Error PDU during DataSet directory: A2 0B 80 02 0A A8 A2 05 A0 03 87 01 02

## Reasons

| Reason | Count |
| --- | ---: |
| application-trigger | 2 |

## Write Steps

| Status | Attribute | Reference | Message |
| --- | --- | --- | --- |
| OK | DefineNamedVariableList | OCR7SR12MEAS/LLN0.AR_DYN_DS01 | MMS DefineNamedVariableList succeeded for OCR7SR12MEAS/LLN0.AR_DYN_DS01. |
| OK | DatSet | OCR7SR12MEAS/LLN0.BR.brcbA01.DatSet [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
| OK | RptEna | OCR7SR12MEAS/LLN0.BR.brcbA01.RptEna [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
| OK | GI | OCR7SR12MEAS/LLN0.BR.brcbA01.GI [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
| OK | RptEna | OCR7SR12MEAS/LLN0.BR.brcbA01.RptEna [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
| OK | DatSet | OCR7SR12MEAS/LLN0.BR.brcbA01.DatSet [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
| OK | DeleteNamedVariableList | OCR7SR12MEAS/LLN0.AR_DYN_DS01 | MMS DeleteNamedVariableList deleted 1 of 1 matched list(s) for OCR7SR12MEAS/LLN0.AR_DYN_DS01. |
