# RCB Reporting Quick Reference and Recovery Contract

> **READ THIS FIRST before changing ARIEC61850 reporting, RCB activation, dynamic DataSet qualification, InformationReport mapping, or BRCB reconnect logic.**

This file is the short mental model for future human and AI development threads. It does not replace the detailed G2 qualification documents; it connects them to a single, vendor-neutral reporting contract and records minimum black-box wire facts that are easy to misinterpret.

Detailed ARIEC61850 evidence and safety contracts remain authoritative for production gating:

- [`G2_EVIDENCE_QUALIFIED_DYNAMIC_REPORTING.md`](G2_EVIDENCE_QUALIFIED_DYNAMIC_REPORTING.md)
- [`G2_4_TRANSACTIONAL_RCB_FIELD_LEASE.md`](G2_4_TRANSACTIONAL_RCB_FIELD_LEASE.md)
- [`G2_6_PRODUCTION_DYNAMIC_CONSUMER.md`](G2_6_PRODUCTION_DYNAMIC_CONSUMER.md)
- [`G2_6_SHADOW_VERIFICATION.md`](G2_6_SHADOW_VERIFICATION.md)
- [`../AGENTS.md`](../AGENTS.md)

The observations below come from controlled black-box interoperability captures reduced to protocol facts. Commercial tool names, raw external captures, proprietary UI, and external implementation structure are deliberately excluded.

## 1. The model to remember

Reporting is a persistent association state machine:

```text
                     ONE MMS ASSOCIATION
                            |
             +--------------+---------------+
             |                              |
    confirmed operations              unconfirmed traffic
    Read / Write / etc.                InformationReport
             |                              |
      invoke-ID routing                no invoke ID
             |                              |
             +--------------+---------------+
                            |
                     report state machine
```

One receive pump owns network reads. It must route confirmed responses and unsolicited reports independently. A report may arrive while a confirmed Read/Write is outstanding.

A successful `RptEna=true` write is **not** proof of report delivery. ARIEC61850 already requires an actual correctly identified/mapped `InformationReport` before report authority or production eligibility is granted.

## 2. Four activation paths

Keep URCB/BRCB and static/dynamic DataSet cases explicit.

| Mode | Compatibility sequence to remember |
|---|---|
| Static DataSet + URCB | `Read RCB -> Resv=true -> RptEna=true -> readback -> listen` |
| Static DataSet + BRCB | `Read RCB -> RptEna=true -> readback -> listen` |
| Dynamic DataSet + URCB | `Define+verify DataSet -> Read RCB -> Resv=true -> configure + RptEna last -> readback -> listen` |
| Dynamic DataSet + BRCB | `Define+verify DataSet -> Read RCB -> configure + RptEna last -> readback -> listen` |

These are observed compatibility patterns, not permission to bypass ARIEC61850's stronger qualification gates.

## 3. Static URCB

Observed sequence:

```text
Read URCB
    -> confirm disabled / reservation free / Owner free
Write Resv=true
Write RptEna=true
Read effective state back
receive InformationReport asynchronously
```

For a populated static URCB, do not rewrite `DatSet`, `TrgOps`, `OptFlds`, or `IntgPd` merely because the client is enabling it.

## 4. Static BRCB

Observed sequence:

```text
Read BRCB
Write RptEna=true
Read effective state back
receive InformationReport asynchronously
```

No separate URCB-style `Resv=true` write was observed. BRCB ownership/reservation-time semantics must not be forced into the URCB reservation model.

## 5. Dynamic DataSet + RCB

ARIEC61850's qualification path is deliberately stricter than the minimum observed client behavior.

Keep the safe DataSet proof:

```text
DefineNamedVariableList
    -> success
GetNamedVariableListAttributes
    -> exact member count
    -> exact member identities
    -> exact member order
```

Then re-read the candidate RCB immediately before mutation.

### Dynamic URCB

```text
Define + verify DataSet
Read candidate URCB
Resv=true
configure while disabled
RptEna=true last
read back effective state
wait for real InformationReport
```

### Dynamic BRCB

```text
Define + verify DataSet
Read candidate BRCB
configure while disabled
RptEna=true last
read back effective state
wait for real InformationReport
```

A controlled reference client used one multi-variable MMS `Write` containing:

```text
DatSet
IntgPd
TrgOps
OptFlds
RptEna=true   <-- final item
```

A compatible implementation must inspect the result for **every** written attribute. Do not treat a grouped MMS Write as application-level atomicity.

ARIEC61850 may keep more conservative transactional field-lease/rollback behavior where required by its safety contract. Wire compatibility does not require weakening the safer state machine.

## 6. Readback is authoritative

Keep three states distinct:

```text
requested value
    !=
Write accepted
    !=
effective RCB readback
```

Controlled evidence showed:

- a server can normalize non-applicable `OptFlds` bits for URCB;
- a BRCB can retain BRCB-specific `BufferOverflow` and `EntryID` options;
- `ConfRev` can advance after DataSet/configuration binding without a direct client write.

Therefore preserve request evidence, per-item Write results, and effective readback separately.

## 7. TrgOps bit numbering

This matches the existing G2.4 correction contract: the six significant `TrgOps` bits have a reserved leading bit.

| BIT STRING bit index | Payload mask | Meaning |
|---:|---:|---|
| 0 | `0x80` | reserved |
| 1 | `0x40` | data-change (`dchg`) |
| 2 | `0x20` | quality-change (`qchg`) |
| 3 | `0x10` | data-update (`dupd`) |
| 4 | `0x08` | integrity / period |
| 5 | `0x04` | general interrogation |

With two trailing unused bits, canonical payload examples already documented by G2.4 include:

```text
02 04 -> GI only
02 40 -> dchg only
02 44 -> dchg + GI
02 7C -> all five standard triggers
```

Preserve raw BER and semantic significant bits separately. Padding-only differences must not be confused with semantic differences.

One compatibility capture contained a requested payload with the reserved high bit set. Treat that as an observed interoperability quirk, not a new semantic definition.

## 8. ReasonForInclusion bit numbering

This is easy to get wrong and should be tested with exact vectors.

The report reason BIT STRING also has a reserved leading significant bit:

| BIT STRING bit index | Payload mask | Meaning |
|---:|---:|---|
| 0 | `0x80` | reserved |
| 1 | `0x40` | data-change |
| 2 | `0x20` | quality-change |
| 3 | `0x10` | data-update |
| 4 | `0x08` | integrity |
| 5 | `0x04` | general interrogation |

Controlled event evidence showed:

```text
ReasonForInclusion payload 0x40
+ sparse inclusion of the changed member
= data-change
```

Controlled periodic evidence showed:

```text
ReasonForInclusion payload 0x08
+ full DataSet inclusion
= integrity
```

**Do not map bit index 0 to data-change.**

If a future profile supports an additional extension/application-trigger reason, add it only from explicit grammar/evidence; never shift the five standard reasons.

## 9. InformationReport mapping

Observed reports used MMS Unconfirmed-PDU `InformationReport` with VMD-specific `variableListName = RPT`.

When `DataReference` is absent, exact DataSet ordering becomes essential:

```text
verified DataSet member order
    + inclusion BIT STRING
    + included AccessResults in order
    = correct report mapping
```

A report member may be a nested MMS `Structure`, not a scalar value. Keep raw/typed projection fail-closed when the structure is not understood.

ReasonForInclusion applies to included members. Sparse event reports must not be expanded into invented values for excluded members.

## 10. Event and integrity scheduling are independent

Controlled BRCB evidence contained both:

- selective data-change reports containing only changed DataSet members; and
- periodic integrity reports containing the full DataSet.

A data-change report was observed immediately before a scheduled integrity report, showing that the event did not reset the integrity cadence.

Use the model:

```text
event trigger ---------+
                       +--> report capture / queue --> delivery
integrity timer -------+
```

## 11. BRCB retained history and reconnect

Controlled reconnect behavior established this useful model:

```text
association lost
    -> previous client delivery enable ends
    -> BRCB configuration remains
    -> DataSet binding remains
    -> report capture continues into retained history
    -> EntryID continues advancing

new association
    -> associate
    -> read BRCB state
    -> choose resume policy
    -> enable delivery
    -> retained reports replay quickly
    -> normal live reporting continues
```

### Replay is burst traffic

Replay is not paced by `IntgPd`. Minutes of retained history may arrive in milliseconds. The receive path and UI/diagnostics path must remain bounded under burst delivery.

### Replay can interleave with confirmed services

Confirmed Read/Write routing and unconfirmed report routing must remain independent during replay.

### EntryID versus SqNum

Observed behavior showed EntryID continuing across reconnect while SqNum restarted for the new delivery stream.

Remember:

```text
SqNum   -> delivery-stream continuity
EntryID -> buffered-record identity / recovery continuity
```

Do not use SqNum as the durable BRCB resume cursor.

### Missing resume anchor means duplicates are possible

When no last-consumed EntryID was supplied, retained history replayed from an older/default point and included a report that had already been received before disconnect.

A production consumer must make its recovery policy explicit:

```text
trusted resume EntryID available
    -> resume after that EntryID when supported
    -> validate continuity

no trusted resume EntryID
    -> accept/diagnose replay from available history
    -> deduplicate by retained-record identity where policy permits

resume EntryID unavailable / overflow gap
    -> expose a continuity gap
    -> do not claim lossless recovery
```

### Byte-stable retained-report evidence

One retained report replayed byte-for-byte identically to its original captured wire encoding. Treat this as strong interoperability evidence, not a universal requirement that every server stores encoded bytes.

### BufferOverflow is record evidence

A replayed report retains its original `BufferOverflow` field. Do not describe it automatically as a new overflow at reconnect time.

## 12. OptFlds reminder

Useful first-octet payload masks:

| Payload mask | Field |
|---:|---|
| `0x40` | sequence number |
| `0x20` | report timestamp |
| `0x10` | reason for inclusion |
| `0x08` | DataSet name |
| `0x04` | data reference |
| `0x02` | buffer overflow |
| `0x01` | EntryID |

`ConfRev` follows in the next significant bit; segmentation is separate.

Non-applicable BRCB fields may be normalized away by an URCB. Always use effective readback rather than assuming the requested bit field became the server state unchanged.

## 13. Production and qualification rules remain stronger than wire parity

This quick reference does **not** relax ARIEC61850 G2 safety rules.

Continue to require:

1. exact identity and ordered-member evidence;
2. fresh RCB availability immediately before mutation;
3. no takeover of occupied/foreign-owned RCBs;
4. bounded DataSet definition qualification;
5. transactional rollback/restore where the workflow changes RCB fields;
6. actual correctly mapped InformationReport proof;
7. polling remains authoritative for unproven points;
8. no uncontrolled retry loop after real dynamic failure;
9. persisted `ProductionEligible` evidence before automatic dynamic production use;
10. explicit continuity diagnostics for BRCB overflow/reconnect gaps.

A mature compatibility client can imitate efficient wire orchestration while keeping stronger safety gates around when that orchestration is allowed.

## 14. AI / developer invariants

Before merging reporting changes, verify these statements are still true:

1. Reporting is a state machine, not one `RptEna` write.
2. One receive pump routes confirmed responses and unsolicited reports concurrently.
3. RCB state is freshly read before mutation.
4. URCB explicit reservation and BRCB enable/recovery are separate workflows.
5. Static RCB binding is not rewritten without explicit intent.
6. Dynamic DataSet identity and order are verified.
7. Configuration occurs while disabled and `RptEna` is the final activation step.
8. Every Write result is checked individually.
9. Effective state is read back after writes.
10. `ConfRev` is server-derived evidence.
11. `TrgOps` bit index 0 is reserved.
12. `ReasonForInclusion` bit index 0 is reserved; `0x40` is data-change and `0x08` is integrity.
13. DataSet order is preserved exactly.
14. Inclusion indexes drive mapping when DataReference is absent.
15. Nested MMS report values fail closed unless their projection is known.
16. Integrity cadence is independent from event reporting.
17. BRCB replay can burst and interleave with confirmed traffic.
18. EntryID is the persistent buffered-record recovery identity; SqNum may restart.
19. Duplicate historical replay is possible without a resume cursor.
20. Overflow/replay gaps remain explicit evidence rather than being hidden by polling or UI state.
21. Capture-specific counts, object names, timing, and bit quirks are never hard-coded as universal IEC 61850 constants.

## 15. Regression vectors worth keeping project-owned

Reconstruct minimal synthetic vectors rather than committing raw external captures:

```text
TrgOps:
    02 04 -> GI
    02 40 -> data-change
    02 7C -> five standard triggers

ReasonForInclusion:
    02 40 -> data-change
    02 20 -> quality-change
    02 10 -> data-update
    02 08 -> integrity
    02 04 -> GI
    02 80 -> reserved/diagnostic, never data-change

Report mapping:
    sparse inclusion + no DataReference -> exact DataSet-index mapping

Async routing:
    InformationReport arrives while confirmed Read is outstanding

BRCB recovery:
    retained EntryID continuity + SqNum restart
    duplicate replay without resume anchor
    explicit gap when resume anchor is unavailable
    historical BufferOverflow retained across replay
```

Any golden bytes added to tests must be project-generated or independently reconstructed from public grammar and minimum protocol facts under the provenance rules in `AGENTS.md`.
