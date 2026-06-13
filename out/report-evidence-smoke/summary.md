# IEC 61850 Report Evidence

- Generated: 2026-06-12 21:09:44.381 UTC
- Target: 192.16.1.157:102
- Mode: mms-report-monitor
- Plan: Report StaticDataSet plan: status=ReadyRequiresWrite, rcb=OCR7SR12PROT/LLN0.BR.brcbA01, dataset=OCR7SR12PROT/LLN0.DataSet, members=2, dynamicPoints=0
- Result: PASS - Static report guarded session complete: writes=3, reports=4, pollReads=4.
- Diagnostics: reports=4, values=6, mappedFailures=0, pollReads=4/4, writeFailures=0, seqGaps=0, seqRegressions=1, entryIdGaps=1, entryIdRegressions=0, duplicates=0, bufOvfl=true
- EntryID: 000000000000000D -> 0000000000000011

## Counts

| Metric | Value |
| --- | ---: |
| Reports | 4 |
| Report values | 6 |
| Mapping failures | 0 |
| Poll reads OK | 4/4 |
| Write failures | 0 |
| Duplicate report keys | 0 |
| Sequence gaps | 0 |
| Sequence regressions | 1 |
| EntryID gaps | 1 |
| EntryID regressions | 0 |
| Buffer overflow observed | true |

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
