# IEC 61850 Report Evidence

- Generated: 2026-06-12 21:53:49.820 UTC
- Target: 192.16.1.157:102
- Mode: mms-report-monitor
- Plan: Report StaticDataSet plan: status=ReadyRequiresWrite, rcb=OCR7SR12PROT/LLN0.BR.brcbA01, dataset=OCR7SR12PROT/LLN0.DataSet, members=2, dynamicPoints=0
- Result: PASS - Static report guarded session complete: writes=3, reports=4, pollReads=56.
- Verification: PASS_WITH_WARNING - verification=PASS_WITH_WARNING, pass=9, warnings=1, failures=0, rcbSnapshots=3, dataSetSnapshots=1
- Diagnostics: diagnostics=PASS_WITH_WARNING, reports=4, values=6, mappedFailures=0, partialMappings=0, pollReads=56/56, writeFailures=0, seqGaps=0, seqResets=1, seqRegressions=0, entryIdGaps=2, entryIdRegressions=0, duplicates=0, bufOvfl=true
- Diagnostic status: PASS_WITH_WARNING
- EntryID: 000000000000000D -> 0000000000000014

## Counts

| Metric | Value |
| --- | ---: |
| Reports | 4 |
| Report values | 6 |
| Mapping failures | 0 |
| Partial mappings | 0 |
| Poll reads OK | 56/56 |
| Write failures | 0 |
| Duplicate report keys | 0 |
| Sequence gaps | 0 |
| Sequence resets | 1 |
| Sequence regressions | 0 |
| EntryID gaps | 2 |
| EntryID regressions | 0 |
| Buffer overflow observed | true |

## Diagnostic Warnings

- BRCB buffer-overflow flag was observed. Treat the session as usable evidence with a warning; check EntryID continuity and relay buffered-report history.
- 1 sequence reset-to-zero event(s) were observed per report stream. This is usually a report burst/GI or vendor sequence reset warning, not a hard failure by itself.
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
| Pass | after-gi | InformationReport | at least 1 report | 4 | InformationReport received. |
| Pass | after-cleanup | OCR7SR12PROT/LLN0.BR.brcbA01.RptEna | false | false | RptEna readback verified. |
| Pass | after-cleanup | OCR7SR12PROT/LLN0.BR.brcbA01.DatSet | OCR7SR12PROT/LLN0.DataSet | OCR7SR12PROT/LLN0.DataSet | RCB.DatSet readback verified. |
| Warning | after-cleanup | OCR7SR12PROT/LLN0.BR.brcbA01.reservation | not active or lease-only | Resv=- ResvTms=42 | BRCB ResvTms lease timer is still visible while RptEna=false. Treat as relay ownership lease/timeout behavior, not cleanup failure. |

### RCB Snapshots

- before: OCR7SR12PROT/LLN0.BR.brcbA01 RptEna=false DatSet=OCR7SR12PROT/LLN0.DataSet Resv=- ResvTms=0 ConfRev=1
- after-enable: OCR7SR12PROT/LLN0.BR.brcbA01 RptEna=true DatSet=OCR7SR12PROT/LLN0.DataSet Resv=- ResvTms=42 ConfRev=1
- after-cleanup: OCR7SR12PROT/LLN0.BR.brcbA01 RptEna=false DatSet=OCR7SR12PROT/LLN0.DataSet Resv=- ResvTms=42 ConfRev=1

### DataSet Snapshots

- before: OCR7SR12PROT/LLN0.DataSet exists members=2 deletable=false

## Report Timeline

| Received UTC | RptID | SqNum | EntryID | BufOvfl | Included | Mapped | Reasons | TimeOfEntry | DataSet |
| --- | --- | ---: | --- | --- | --- | ---: | --- | --- | --- |
| 2026-06-12 21:52:49.777 | OCR7SR12PROT/LLN0$BR$brcbA01 | 1 | 000000000000000D | true | [0] | 1 | quality-change | 2026-06-12 20:55:08.659 UTC (binary-time=047D1E733C8F) | OCR7SR12PROT/LLN0$DataSet |
| 2026-06-12 21:52:49.789 | OCR7SR12PROT/LLN0$BR$brcbA01 | 1 | 000000000000000F | false | [1] | 1 | quality-change | 2026-06-12 20:55:17.702 UTC (binary-time=047D41C63C8F) | OCR7SR12PROT/LLN0$DataSet |
| 2026-06-12 21:52:49.789 | OCR7SR12PROT/LLN0$BR$brcbA01 | 0 | 0000000000000013 | false | [0,1] | 2 | application-trigger | 2026-06-12 21:43:20.210 UTC (binary-time=04A93D923C8F) | OCR7SR12PROT/LLN0$DataSet |
| 2026-06-12 21:52:49.790 | OCR7SR12PROT/LLN0$BR$brcbA01 | 0 | 0000000000000014 | false | [0,1] | 2 | application-trigger | 2026-06-12 21:52:49.770 UTC (binary-time=04B1EE6A3C8F) | OCR7SR12PROT/LLN0$DataSet |

## Reasons

| Reason | Count |
| --- | ---: |
| application-trigger | 4 |
| quality-change | 2 |

## Write Steps

| Status | Attribute | Reference | Message |
| --- | --- | --- | --- |
| OK | RptEna | OCR7SR12PROT/LLN0.BR.brcbA01.RptEna [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
| OK | GI | OCR7SR12PROT/LLN0.BR.brcbA01.GI [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
| OK | RptEna | OCR7SR12PROT/LLN0.BR.brcbA01.RptEna [BR] | MMS Confirmed-Write succeeded for 1 item(s). |
