# IEC 61850 Report Evidence

- Generated: 2026-06-13 00:13:25.949 UTC
- Target: 192.16.1.157:102
- Mode: mms-report-monitor
- Plan: Report StaticDataSet plan: status=ReadyRequiresWrite, rcb=OCR7SR12PROT/LLN0.BR.brcbB01, dataset=OCR7SR12PROT/LLN0.DataSet, members=2, dynamicPoints=0
- Result: FAIL - RptEna=true failed; report session was not started.
- Verification: FAIL - verification=FAIL, pass=4, warnings=0, failures=1, rcbSnapshots=1, dataSetSnapshots=1
- Diagnostics: diagnostics=PASS, reports=0, values=0, mappedFailures=0, partialMappings=0, pollReads=0/0, writeFailures=0, seqGaps=0, seqResets=0, seqRegressions=0, entryIdGaps=0, entryIdRegressions=0, duplicates=0, bufOvfl=false
- Diagnostic status: PASS
- EntryID: - -> -
- Duration: 0 s
- Soak snapshots: 0

## Smart RCB Selection

- RCB selection: mode=StaticDataSet, selected=OCR7SR12PROT/LLN0.BR.brcbB01, preferred=-, strict=false, fallbackUsed=no, candidates=286

| Decision | RCB | Score | Availability | Reason |
| --- | --- | ---: | --- | --- |
| Selected | BRCB OCR7SR12PROT/LLN0.BR.brcbB01 | 345 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/LLN0.RP.urcbB01 | 305 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/LLN0.RP.urcbC01 | 305 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/LLN0.RP.urcbD01 | 305 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/LLN0.RP.urcbE01 | 305 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/LLN0.RP.urcbF01 | 305 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/LLN0.RP.urcbG01 | 305 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/LLN0.RP.urcbH01 | 305 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/LLN0.RP.urcbI01 | 305 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/LLN0.RP.urcbJ01 | 305 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/A2759PTOV1.RP.urcbA01 | 290 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/A2759PTOV1.RP.urcbB01 | 290 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/A2759PTOV2.RP.urcbA01 | 290 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/A2759PTOV2.RP.urcbB01 | 290 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/A2759PTOV3.RP.urcbA01 | 290 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/A2759PTOV3.RP.urcbB01 | 290 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/A2759PTOV4.RP.urcbA01 | 290 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/A2759PTOV4.RP.urcbB01 | 290 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/A2759PTUV1.RP.urcbA01 | 290 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| Candidate | URCB OCR7SR12PROT/A2759PTUV1.RP.urcbB01 | 290 | AvailableStatic | Static RCB has DatSet, RptEna=false, no active reservation, and DataSet directory is usable. |
| ... | +266 more RCB candidates |  |  | See rcb-candidates.json | 

## Counts

| Metric | Value |
| --- | ---: |
| Reports | 0 |
| Report values | 0 |
| Mapping failures | 0 |
| Partial mappings | 0 |
| Poll reads OK | 0/0 |
| Write failures | 0 |
| Duplicate report keys | 0 |
| Sequence gaps | 0 |
| Sequence resets | 0 |
| Sequence regressions | 0 |
| EntryID gaps | 0 |
| EntryID regressions | 0 |
| Buffer overflow observed | false |

## Verification

- Status: FAIL
- Summary: verification=FAIL, pass=4, warnings=0, failures=1, rcbSnapshots=1, dataSetSnapshots=1

| Severity | Stage | Target | Expected | Observed | Message |
| --- | --- | --- | --- | --- | --- |
| Pass | before | OCR7SR12PROT/LLN0.BR.brcbB01.RptEna | false | false | RptEna readback verified. |
| Pass | before | OCR7SR12PROT/LLN0.BR.brcbB01.DatSet | OCR7SR12PROT/LLN0.DataSet | OCR7SR12PROT/LLN0.DataSet | RCB.DatSet readback verified. |
| Pass | before | OCR7SR12PROT/LLN0.BR.brcbB01.reservation | not active | Resv=- ResvTms=0 | RCB reservation readback verified as inactive. |
| Pass | before | OCR7SR12PROT/LLN0.DataSet | 2 member(s) in requested order | 2 member(s) | DataSet directory readback verified. |
| Fail | after-enable | OCR7SR12PROT/LLN0.BR.brcbB01.RptEna | write accepted | MMS Confirmed-Write returned 1 failure(s). | RptEna=true write failed. |

### RCB Snapshots

- before: OCR7SR12PROT/LLN0.BR.brcbB01 RptEna=false DatSet=OCR7SR12PROT/LLN0.DataSet Resv=- ResvTms=0 ConfRev=1

### DataSet Snapshots

- before: OCR7SR12PROT/LLN0.DataSet exists members=2 deletable=false

## Write Steps

| Status | Attribute | Reference | Message |
| --- | --- | --- | --- |
| FAIL | RptEna | OCR7SR12PROT/LLN0.BR.brcbB01.RptEna [BR] | MMS Confirmed-Write returned 1 failure(s). |
