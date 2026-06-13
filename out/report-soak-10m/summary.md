# IEC 61850 Report Evidence

- Generated: 2026-06-12 22:30:24.401 UTC
- Target: 192.16.1.157:102
- Mode: mms-report-monitor
- Plan: Report StaticDataSet plan: status=ReadyRequiresWrite, rcb=OCR7SR12PROT/LLN0.BR.brcbA01, dataset=OCR7SR12PROT/LLN0.DataSet, members=2, dynamicPoints=0
- Result: PASS - Static report guarded session complete: writes=7, reports=10, pollReads=557.
- Verification: PASS_WITH_WARNING - verification=PASS_WITH_WARNING, pass=9, warnings=1, failures=0, rcbSnapshots=3, dataSetSnapshots=1
- Diagnostics: diagnostics=PASS_WITH_WARNING, reports=10, values=16, mappedFailures=0, partialMappings=0, pollReads=557/557, writeFailures=0, seqGaps=0, seqResets=2, seqRegressions=0, entryIdGaps=2, entryIdRegressions=0, duplicates=0, bufOvfl=true
- Diagnostic status: PASS_WITH_WARNING
- EntryID: 000000000000000D -> 000000000000001C
- Duration: 600.036 s
- Soak snapshots: 11

## Counts

| Metric | Value |
| --- | ---: |
| Reports | 10 |
| Report values | 16 |
| Mapping failures | 0 |
| Partial mappings | 0 |
| Poll reads OK | 557/557 |
| Write failures | 0 |
| Duplicate report keys | 0 |
| Sequence gaps | 0 |
| Sequence resets | 2 |
| Sequence regressions | 0 |
| EntryID gaps | 2 |
| EntryID regressions | 0 |
| Buffer overflow observed | true |

## Diagnostic Warnings

- BRCB buffer-overflow flag was observed. Treat the session as usable evidence with a warning; check EntryID continuity and relay buffered-report history.
- 2 sequence reset-to-zero event(s) were observed per report stream. This is usually a report burst/GI or vendor sequence reset warning, not a hard failure by itself.
- 2 numeric EntryID gap(s) were observed. EntryID is treated as opaque by default; numeric gap is a heuristic warning, not a hard failure.

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
| Pass | after-gi | InformationReport | at least 1 report | 10 | InformationReport received. |
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
| 2026-06-12 22:20:24.352 | 0 | 0 | 0 | 0/0 | 0 | 6 | Receive pump completed ConfirmedResponse for invokeID=2714. queuedReports=6. |
| 2026-06-12 22:21:24.380 | 60.028 | 6 | 8 | 55/55 | 0 | 0 | Receive pump completed ConfirmedResponse for invokeID=2769. queuedReports=0. |
| 2026-06-12 22:22:24.425 | 120.073 | 6 | 8 | 111/111 | 0 | 0 | Receive pump completed ConfirmedResponse for invokeID=2825. queuedReports=0. |
| 2026-06-12 22:23:24.487 | 180.135 | 7 | 10 | 167/167 | 0 | 0 | Receive pump completed ConfirmedResponse for invokeID=2882. queuedReports=0. |
| 2026-06-12 22:24:24.531 | 240.179 | 7 | 10 | 223/223 | 0 | 0 | Receive pump completed ConfirmedResponse for invokeID=2938. queuedReports=0. |
| 2026-06-12 22:25:24.598 | 300.246 | 8 | 12 | 279/279 | 0 | 0 | Receive pump completed ConfirmedResponse for invokeID=2995. queuedReports=0. |
| 2026-06-12 22:26:24.668 | 360.315 | 8 | 12 | 336/336 | 0 | 1 | Receive pump completed ConfirmedResponse for invokeID=3053. queuedReports=0. |
| 2026-06-12 22:27:24.730 | 420.378 | 9 | 14 | 392/392 | 0 | 0 | Receive pump completed ConfirmedResponse for invokeID=3109. queuedReports=0. |
| 2026-06-12 22:28:24.765 | 480.413 | 10 | 16 | 448/448 | 0 | 0 | Dequeued queued InformationReport. queuedReports=0. |
| 2026-06-12 22:29:24.794 | 540.442 | 10 | 16 | 502/502 | 0 | 0 | Receive pump completed ConfirmedResponse for invokeID=3220. queuedReports=0. |
| 2026-06-12 22:30:24.364 | 600.036 | 10 | 16 | 557/557 | 0 | 0 | Receive pump completed ConfirmedResponse for invokeID=3285. queuedReports=0. |

## Report Timeline

| Received UTC | RptID | SqNum | EntryID | BufOvfl | Included | Mapped | Reasons | TimeOfEntry | DataSet |
| --- | --- | ---: | --- | --- | --- | ---: | --- | --- | --- |
| 2026-06-12 22:20:24.356 | OCR7SR12PROT/LLN0$BR$brcbA01 | 1 | 000000000000000D | true | [0] | 1 | quality-change | 2026-06-12 20:55:08.659 UTC (binary-time=047D1E733C8F) | OCR7SR12PROT/LLN0$DataSet |
| 2026-06-12 22:20:24.368 | OCR7SR12PROT/LLN0$BR$brcbA01 | 1 | 000000000000000F | false | [1] | 1 | quality-change | 2026-06-12 20:55:17.702 UTC (binary-time=047D41C63C8F) | OCR7SR12PROT/LLN0$DataSet |
| 2026-06-12 22:20:24.368 | OCR7SR12PROT/LLN0$BR$brcbA01 | 0 | 0000000000000015 | false | [0,1] | 2 | application-trigger | 2026-06-12 22:09:39.551 UTC (binary-time=04C156DF3C8F) | OCR7SR12PROT/LLN0$DataSet |
| 2026-06-12 22:20:24.368 | OCR7SR12PROT/LLN0$BR$brcbA01 | 1 | 0000000000000016 | false | [0] | 1 | quality-change | 2026-06-12 22:10:17.992 UTC (binary-time=04C1ED083C8F) | OCR7SR12PROT/LLN0$DataSet |
| 2026-06-12 22:20:24.368 | OCR7SR12PROT/LLN0$BR$brcbA01 | 2 | 0000000000000017 | false | [0] | 1 | quality-change | 2026-06-12 22:10:24.482 UTC (binary-time=04C206623C8F) | OCR7SR12PROT/LLN0$DataSet |
| 2026-06-12 22:20:24.368 | OCR7SR12PROT/LLN0$BR$brcbA01 | 0 | 0000000000000018 | false | [0,1] | 2 | application-trigger | 2026-06-12 22:20:24.349 UTC (binary-time=04CB2D9D3C8F) | OCR7SR12PROT/LLN0$DataSet |
| 2026-06-12 22:22:24.431 | OCR7SR12PROT/LLN0$BR$brcbA01 | 1 | 0000000000000019 | false | [0,1] | 2 | application-trigger | 2026-06-12 22:22:24.427 UTC (binary-time=04CD02AB3C8F) | OCR7SR12PROT/LLN0$DataSet |
| 2026-06-12 22:24:24.535 | OCR7SR12PROT/LLN0$BR$brcbA01 | 2 | 000000000000001A | false | [0,1] | 2 | application-trigger | 2026-06-12 22:24:24.533 UTC (binary-time=04CED7D53C8F) | OCR7SR12PROT/LLN0$DataSet |
| 2026-06-12 22:26:24.669 | OCR7SR12PROT/LLN0$BR$brcbA01 | 3 | 000000000000001B | false | [0,1] | 2 | application-trigger | 2026-06-12 22:26:24.557 UTC (binary-time=04D0ACAD3C8F) | OCR7SR12PROT/LLN0$DataSet |
| 2026-06-12 22:28:24.661 | OCR7SR12PROT/LLN0$BR$brcbA01 | 4 | 000000000000001C | false | [0,1] | 2 | application-trigger | 2026-06-12 22:28:24.657 UTC (binary-time=04D281D13C8F) | OCR7SR12PROT/LLN0$DataSet |

## Reasons

| Reason | Count |
| --- | ---: |
| application-trigger | 12 |
| quality-change | 4 |

## Write Steps

| Status | Attribute | Reference | Message |
| --- | --- | --- | --- |
| OK | RptEna | OCR7SR12PROT/LLN0.BR.brcbA01.RptEna [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
| OK | GI | OCR7SR12PROT/LLN0.BR.brcbA01.GI [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
| OK | GI(periodic) | OCR7SR12PROT/LLN0.BR.brcbA01.GI [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
| OK | GI(periodic) | OCR7SR12PROT/LLN0.BR.brcbA01.GI [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
| OK | GI(periodic) | OCR7SR12PROT/LLN0.BR.brcbA01.GI [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
| OK | GI(periodic) | OCR7SR12PROT/LLN0.BR.brcbA01.GI [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
| OK | RptEna | OCR7SR12PROT/LLN0.BR.brcbA01.RptEna [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
