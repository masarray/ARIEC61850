# P9 — Reliable four-RCB dispatch candidate (2026-10-31)

## Proven source-level defects, not an assumed SG7 root cause

**Multi-monitor activation ordering:** `StartPersistentReportMonitorAsync` previously sent successful `RptEna`, performed post-enable readback and issued GI **before** registering its monitor target. The canonical MMS FIFO retains unsolicited frames, but another RCB's concurrent slice can drain it before the new RCB is registered. Such early initial-image frames then have no matching active target and cannot become values. This patch registers the monitor after acknowledged enable, **before** readback and GI. Exceptions after registration unregister the target. A Report arriving before the RptEna acknowledgment remains a known, deliberately unproven boundary: it cannot yet count as an authorized activation.

**Unbounded per-RCB pending delivery:** the canonical receive FIFO already has count/byte limits, but the post-routing `ConcurrentQueue<MmsReportFrame>` had none. P9 atomically reserves bounded space (default 2048) before enqueue and never blocks the dispatcher. At capacity it retains the oldest frames, rejects new deliveries, increments a sticky per-RCB overflow counter, and exposes unverified continuity through the monitor summary and receive-slice result. Draining cannot erase evidence of a gap. Other RCBs remain independent. No implicit MMS polling, GI retry, mutation of RCB ownership, or vendor-specific exception was introduced.

## Deterministic synthetic regression

`MmsP9MultiRcbDispatchTests` uses original, synthetic BER-encoded MMS InformationReport PDUs and exercises decode, multi-RCB identity routing, typed member projection and bounded delivery. Cases include four independent SG7-style RCBs, abbreviated and full DataSet identities, burst ordering, contradictory RptID/DatSet quarantine, zero-frame activation, one slow RCB overflow, and unaffected peers. No external captures or proprietary implementation material was used.

## Production gate

This is an engine candidate, **not** proof that physical SG7 recovered from 56/77 to 77/77 or that all 4 RCB streams resumed. Confirm physical post-enable readback, ingress vs route vs projection, association lifetime, real first Report, and BufOvfl/EntryID/SqNum before closing ARSAS #707. P8 trial PR #763, ARSAS main/RC, v1.6.42 and pinned baseline remain untouched. A new Windows P9 artifact requires an exact-SHA ARSAS integration gate; this engine PR alone is not a Windows release.


## P9 read-mostly registry and reproducible performance evidence

The previous per-frame dispatch called `SnapshotPersistentReportMonitors` under a registry lock and constructed a new `Where(...).ToArray()` even if the exact same four RCBs had been active for hours. This is a verified, avoidable allocation and lock on the MMS hot path.

P9 now publishes immutable array snapshots only on membership mutations (activate, unregister, association reset) with a volatile publication/read. This removes the registry lock and repeated snapshot allocation from ordinary receive dispatch. Routing selection and typed projection are deliberately unchanged to preserve the proven fail-closed identity behavior. On association reset, the old monitors are marked stopped and the active snapshot is atomically cleared before draining; draining a slow monitor does not hold the registry lock.

The xUnit P9 performance fixture produces local timing and `GC.GetAllocatedBytesForCurrentThread()` counts for a synthetic warm four-RCB MMS BER routing workload. Wire PDUs are prebuilt outside the measurement. One strict *structural* assertion verifies stable snapshot reads allocate at most a negligible fixed runtime overhead; **no speed/throughput SLA is asserted on shared GitHub Actions hardware**. Interpret latency and allocated bytes/frame only comparatively at matched exact commit/runtime/host. This is not first-report physical latency, full socket throughput, process RSS, lossless SOE, or a 77/77 field acceptance claim.

Regression also verifies no snapshot republish for duplicate registration, the published registry is empty after association reset, all old RCB targets are stopped and pending queues drained, and old session frames cannot be admitted again. Future optimization of the identity selector should use these baseline metrics and retain the ambiguous identity tests rather than introducing vendor shortcuts.
