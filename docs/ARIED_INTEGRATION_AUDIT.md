# ARIEC61850 integration audit for ArIED 61850 Tester 1.4.0

This document records the source-level findings from the ArIED 1.4.0 patch review and the changes integrated into the reusable ARIEC61850 engine.

## Integrated findings

### 1. Live IED identity

The current branch already resolves identity from the full set of MMS Logical Device domains. It no longer truncates product names at the first digit, for example `OLSF501` becoming `OLSF`. A single-domain split remains a heuristic unless SCL or configured identity evidence is available.

### 2. Multi-RCB report routing

Persistent report monitors now register per association. Each InformationReport is decoded for header identity before value projection and routed by this order:

1. exact `RptID`;
2. RCB-name affinity;
3. exact DataSet reference;
4. unique DataSet tail;
5. single-monitor fallback.

Ambiguous frames are not projected against an arbitrary DataSet. The session increments `UnroutedPersistentReportCount` and exposes the routing diagnostic.

### 3. Quality decoding

IEC 61850 quality decoding now requires a bit-string payload with at least 13 meaningful bits. Short Dbpos/control bit strings are not misclassified as quality.

### 4. Partial `q`/`t` report updates

Report projection now preserves frames containing only quality or timestamp companions. `MmsReportSignalUpdate` exposes `HasValue`, `HasQuality`, and `HasTimestamp`, allowing consumers to merge companions without replacing the current process value.

### 5. Simulator ObjectName interoperability

The read-only MMS simulator accepts domain-specific and VMD/AA/ISO `ObjectName` scopes for `GetVariableAccessAttributes`. A VMD-specific probe receives a conservative structure response rather than causing the association to close.

## Remaining live validation

The following still require a real target relay or IEDScout session: concurrent vendor RCB streams, segmented reports, sequence wrap, BRCB EntryID resume, dynamic DataSet cleanup, and confirmation of the actual `RptID` values emitted by each vendor.
