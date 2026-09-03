# G2.6 — Dynamic Report Shadow Verification

## Purpose

G2.6 shadow verification is the final evidence layer between an `InformationReportProven` device and any later `ProductionEligible` decision.

It does **not** enable production dynamic reporting. It does **not** mutate an RCB, DataSet, or qualification profile. The engine only evaluates observations collected by a commissioning consumer.

The shadow contract is:

`dynamic InformationReport candidate -> independent MMS reference read -> exact identity/value/quality/timestamp/order/reconnect comparison -> typed acceptance evidence`

The report path may be the intended production acquisition path, but during shadow verification independent MMS reads remain the authority used to detect missing, stale, duplicated, mis-mapped, or divergent report traffic.

## Exact identity boundary

Every observation is bound to the already-qualified DataSet member sequence by both:

- exact DataSet index; and
- normalized exact MMS member reference.

No fuzzy match, vendor alias, neighboring DataObject guess, or alternate RCB substitution is permitted.

Multiple included members from one InformationReport may share the same report sequence number. This is valid. A decreasing sequence number is an ordering failure, while an exact duplicate `(sequence number, DataSet index)` is rejected separately.

## Evidence collected by the consumer

`MmsDynamicReportShadowVerificationEvidence` contains:

- immutable evidence ID and capture timestamp;
- exact qualified member sequence;
- report observations: index, member, value, quality, optional device timestamp, local receive time, optional sequence number;
- independent polling observations: index, member, value, quality, optional device timestamp, read time;
- reconnect attempts and successful reconnects;
- report resubscription evidence after reconnect;
- polling-reference recovery evidence after reconnect;
- total dynamic activation attempts.

The engine performs no network I/O while evaluating this object.

## Required shadow gates

`MmsDynamicReportShadowVerificationPolicy.Evaluate(...)` fails closed unless the configured gates close:

1. **Exact member identity** — every report/poll sample maps to the same qualified DataSet index/member identity.
2. **Value parity** — each accepted report observation has a bounded later independent MMS read with the same process value.
3. **Quality parity** — when quality evidence is present, report and polling quality must agree. Commissioning may require quality evidence explicitly.
4. **Timestamp parity** — when device timestamps are present, both sides must be present and within the configured tolerance. Commissioning may require timestamp evidence explicitly.
5. **Report order** — receive time may not regress and report sequence number may not decrease.
6. **No duplicate report edges** — the same sequence number and DataSet index may not appear more than once.
7. **No missing report edges** — every value transition independently observed by polling must have a matching report observation inside the bounded correlation window.
8. **Polling authority guard** — polling must remain available as an independent reference for every accepted report observation during the shadow.
9. **Reconnect regression** — when required, every deliberate reconnect must recover both report subscription and the independent polling reference.
10. **No repeated mutation loop** — dynamic activation attempts are bounded per association; reconnect recovery must not cause uncontrolled RCB/DataSet rewrite loops.

Any failed gate produces a typed failure result and cannot be converted into production acceptance evidence.

## Production acceptance bridge

A successful shadow result can be converted through:

`MmsDynamicReportShadowVerificationPolicy.BuildProductionAcceptance(...)`

This creates the existing `MmsDynamicReportProductionAcceptance` contract. It deliberately still requires the caller to provide two independent regression decisions:

- Smart Control regression; and
- static reporting regression.

The shadow result supplies evidence for:

- dynamic InformationReport regression;
- polling-authority guard;
- reconnect regression;
- quality/timestamp regression; and
- no repeated mutation loop.

Creating this acceptance record still does **not** change profile state. The caller must separately invoke `MmsDynamicReportQualificationProfilePolicy.MarkProductionEligible(...)`, and that policy continues to require an identity-compatible `InformationReportProven` profile plus every production gate passing.

## Intended ARSAS commissioning flow

The consumer integration should remain explicit and fail closed:

1. load the exact identity-compatible `InformationReportProven` profile;
2. use only the exact proven RCB/member sequence;
3. arm the dynamic report path with the established safety/cleanup contract;
4. run an isolated read-only MMS reference association against the same exact members;
5. collect report and polling observations during controlled normal process transitions;
6. perform one deliberate reconnect cycle and prove both paths recover;
7. stop and prove RCB/DataSet cleanup where the commissioning transaction requires it;
8. evaluate the typed shadow evidence;
9. retain `InformationReportProven` on any failure;
10. consider `ProductionEligible` only after shadow PASS plus independent control/static-reporting acceptance.

Production automatic dynamic reporting must remain OFF until the persisted profile is explicitly and validly advanced to `ProductionEligible`.
