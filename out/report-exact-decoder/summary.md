# IEC 61850 Report Evidence

- Generated: 2026-06-12 22:38:45.836 UTC
- Target: 192.16.1.157:102
- Mode: mms-report-monitor
- Plan: Report StaticDataSet plan: status=ReadyRequiresWrite, rcb=OCR7SR12PROT/LLN0.BR.brcbA01, dataset=OCR7SR12PROT/LLN0.DataSet, members=2, dynamicPoints=0
- Result: PASS - Static report guarded session complete: writes=3, reports=6, pollReads=56.
- Verification: PASS_WITH_WARNING - verification=PASS_WITH_WARNING, pass=9, warnings=1, failures=0, rcbSnapshots=3, dataSetSnapshots=1
- Diagnostics: diagnostics=PASS_WITH_WARNING, reports=6, values=8, mappedFailures=0, partialMappings=0, pollReads=56/56, writeFailures=0, seqGaps=1, seqResets=1, seqRegressions=0, entryIdGaps=3, entryIdRegressions=0, duplicates=0, bufOvfl=true
- Diagnostic status: PASS_WITH_WARNING
- EntryID: 000000000000000D -> 000000000000001D
- Duration: 60.036 s
- Soak snapshots: 2

## Counts

| Metric | Value |
| --- | ---: |
| Reports | 6 |
| Report values | 8 |
| Mapping failures | 0 |
| Partial mappings | 0 |
| Poll reads OK | 56/56 |
| Write failures | 0 |
| Duplicate report keys | 0 |
| Sequence gaps | 1 |
| Sequence resets | 1 |
| Sequence regressions | 0 |
| EntryID gaps | 3 |
| EntryID regressions | 0 |
| Buffer overflow observed | true |

## Diagnostic Warnings

- BRCB buffer-overflow flag was observed. Treat the session as usable evidence with a warning; check EntryID continuity and relay buffered-report history.
- 1 sequence gap(s) were observed per report stream.
- 1 sequence reset-to-zero event(s) were observed per report stream. This is usually a report burst/GI or vendor sequence reset warning, not a hard failure by itself.
- 3 numeric EntryID gap(s) were observed. EntryID is treated as opaque by default; numeric gap is a heuristic warning, not a hard failure.

## Verification

- Status: PASS_WITH_WARNING
- Summary: verification=PASS_WITH_WARNING, pass=9, warnings=1, failures=0, rcbSnapshots=3, dataSetSnapshots=1

| Severity | Stage | Target | Expected | Observed | Message |
| --- | --- | --- | --- | --- | --- |
| Pass | before | OCR7SR12PROT/LLN0.BR.brcbA01.RptEna | false | false | RptEna readback verified. |
| Pass | before | OCR7SR12PROT/LLN0.BR.brcbA01.DatSet | OCR7SR12PROT/LLN0.DataSet | OCR7SR12PROT/LLN0.DataSet | RCB.DatSet readback verified. |
| Pass | before | OCR7SR12PROT/LLN0.BR.brcbA01.reservation | not active | Resv=- ResvTms=0 | RCB reservation readback verified as inactive. |
| Pass | before | OCR7SR12PROT/LLN0.DataSet | 2 member(s) in requested order | 2 member(s) | DataSet directory readback verified. |
| Pass | after-enable | OCR7SR12PROT/LLN0.BR.brcbA01.RptEna | true | true | RptEna readback verified. |
| Pass | after-enable | OCR7SR12PROT/LLN0.BR.brcbA01.DatSet | OCR7SR12PROT/LLN0.DataSet | OCR7SR12PROT/LLN0.DataSet | RCB.DatSet readback verified. |
| Pass | after-gi | InformationReport | at least 1 report | 6 | InformationReport received. |
| Pass | after-cleanup | OCR7SR12PROT/LLN0.BR.brcbA01.RptEna | false | false | RptEna readback verified. |
| Pass | after-cleanup | OCR7SR12PROT/LLN0.BR.brcbA01.DatSet | OCR7SR12PROT/LLN0.DataSet | OCR7SR12PROT/LLN0.DataSet | RCB.DatSet readback verified. |
| Warning | after-cleanup | OCR7SR12PROT/LLN0.BR.brcbA01.reservation | not active or lease-only | Resv=- ResvTms=42 | BRCB ResvTms lease timer is still visible while RptEna=false. Treat as relay ownership lease/timeout behavior, not cleanup failure. |

### RCB Snapshots

- before: OCR7SR12PROT/LLN0.BR.brcbA01 RptEna=false DatSet=OCR7SR12PROT/LLN0.DataSet Resv=- ResvTms=0 ConfRev=1
- after-enable: OCR7SR12PROT/LLN0.BR.brcbA01 RptEna=true DatSet=OCR7SR12PROT/LLN0.DataSet Resv=- ResvTms=42 ConfRev=1
- after-cleanup: OCR7SR12PROT/LLN0.BR.brcbA01 RptEna=false DatSet=OCR7SR12PROT/LLN0.DataSet Resv=- ResvTms=42 ConfRev=1

### DataSet Snapshots

- before: OCR7SR12PROT/LLN0.DataSet exists members=2 deletable=false

## Soak Snapshots

| Captured UTC | Elapsed s | Reports | Values | Poll OK | Pending | Queued reports | Routing | 
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- | 
| 2026-06-12 22:37:45.794 | 0 | 0 | 0 | 0/0 | 0 | 6 | Receive pump completed ConfirmedResponse for invokeID=2714. queuedReports=6. |
| 2026-06-12 22:38:45.805 | 60.036 | 6 | 8 | 56/56 | 0 | 0 | Receive pump completed ConfirmedResponse for invokeID=2780. queuedReports=0. |

## Report Timeline

| Received UTC | RptID | Decoder | SqNum | EntryID | BufOvfl | Included | Mapped | Reasons | TimeOfEntry | DataSet |
| --- | --- | --- | ---: | --- | --- | --- | ---: | --- | --- | --- |
| 2026-06-12 22:37:45.797 | OCR7SR12PROT/LLN0$BR$brcbA01 | 1 | 000000000000000D | true | [0] | 1 | quality-change | 2026-06-12 20:55:08.659 UTC (binary-time=047D1E733C8F) | OCR7SR12PROT/LLN0$DataSet |
| 2026-06-12 22:37:45.811 | OCR7SR12PROT/LLN0$BR$brcbA01 | 1 | 000000000000000F | false | [1] | 1 | quality-change | 2026-06-12 20:55:17.702 UTC (binary-time=047D41C63C8F) | OCR7SR12PROT/LLN0$DataSet |
| 2026-06-12 22:37:45.811 | OCR7SR12PROT/LLN0$BR$brcbA01 | 1 | 0000000000000016 | false | [0] | 1 | quality-change | 2026-06-12 22:10:17.992 UTC (binary-time=04C1ED083C8F) | OCR7SR12PROT/LLN0$DataSet |
| 2026-06-12 22:37:45.811 | OCR7SR12PROT/LLN0$BR$brcbA01 | 2 | 0000000000000017 | false | [0] | 1 | quality-change | 2026-06-12 22:10:24.482 UTC (binary-time=04C206623C8F) | OCR7SR12PROT/LLN0$DataSet |
| 2026-06-12 22:37:45.811 | OCR7SR12PROT/LLN0$BR$brcbA01 | 4 | 000000000000001C | false | [0,1] | 2 | application-trigger | 2026-06-12 22:28:24.657 UTC (binary-time=04D281D13C8F) | OCR7SR12PROT/LLN0$DataSet |
| 2026-06-12 22:37:45.811 | OCR7SR12PROT/LLN0$BR$brcbA01 | 0 | 000000000000001D | false | [0,1] | 2 | application-trigger | 2026-06-12 22:37:45.790 UTC (binary-time=04DB11BE3C8F) | OCR7SR12PROT/LLN0$DataSet |

## Reasons

| Reason | Count |
| --- | ---: |
| application-trigger | 4 |
| quality-change | 4 |

## Write Steps

| Status | Attribute | Reference | Message |
| --- | --- | --- | --- |
| OK | RptEna | OCR7SR12PROT/LLN0.BR.brcbA01.RptEna [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
| OK | GI | OCR7SR12PROT/LLN0.BR.brcbA01.GI [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
| OK | RptEna | OCR7SR12PROT/LLN0.BR.brcbA01.RptEna [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
