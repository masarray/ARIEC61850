# G2 — Evidence-Qualified Smart Dynamic Reporting

This document is the engineering contract for enabling dynamic IEC 61850 reporting after the ARSAS 1.6.32 / ARIEC61850 G1.1 field-proven baseline.

## Frozen baseline

The G2 program starts from the consolidated ARIEC61850 `main` baseline that contains the field-proven G1/G1.1 control path and the P6.2-B reporting stability quarantine.

The following behavior is treated as a non-regression contract throughout G2:

1. Smart Control keeps StationControl-origin SBOw -> Operate ordering, positive CommandTermination handling and process-feedback correlation.
2. Proven static BRCB/URCB coverage always wins before any dynamic residual coverage.
3. A selected point does not leave fast MMS verification/fallback until actual report delivery for that point has been proven.
4. G2 must not change the bounded reconnect/backoff/warm-up semantics owned by ARSAS P6.2-C.
5. IEC quality is preserved exactly; questionable/invalid evidence is never cosmetically forced to Good.
6. IEC references remain exact/literal. No fuzzy mapping is introduced to improve apparent coverage.
7. Qualification failure must not destabilize the production monitoring association outside an explicit commissioning workflow.
8. Dynamic success means a correctly identified and mapped InformationReport was received. `DefineNamedVariableList`, `RptEna=true`, or GI acceptance alone are not production proof.
9. A real dynamic failure remains stronger evidence than advertised capability and must prevent uncontrolled repeated mutation.

## Why G2 exists

Physical SIPROTEC evidence proved that a one-member temporary NamedVariableList can be defined, read back, and deleted successfully while a later full dynamic DataSet operation can still abort the MMS association.

Therefore:

- an advertised service bit is not enough;
- a successful one-member probe is not enough;
- a fixed planner limit such as 64 members is not an IED capability claim;
- a free RCB is not permission to mutate it automatically;
- production dynamic reporting must be derived from measured evidence.

## Qualification state model

Dynamic capability progresses only through explicit evidence states:

1. `Advertised` — MMS negotiation and fresh RCB evidence indicate the relevant services/attributes may be usable.
2. `SingleMemberProven` — one exact member completes Define -> GetAttributes -> Delete with association survival and successful cleanup.
3. `EnvelopeQualified` — bounded multi-member trials establish a safe member/PDU envelope for the exact IED profile.
4. `RcbActivationProven` — one qualified small DataSet can be bound to a verified-free RCB and enabled without destabilizing the association.
5. `InformationReportProven` — the enabled RCB produces an actual correctly identified/mapped InformationReport.
6. `ProductionEligible` — all required non-regression and persistence gates are satisfied and the profile may be consumed by the production planner.

No state may be inferred from a later-looking side effect. Each transition requires its own evidence.

## G2.1 — bounded multi-member qualification primitive

Status target: engine-only, qualification-only, no production behavior change.

The qualification transaction is:

`DefineNamedVariableList -> GetNamedVariableListAttributes -> DeleteNamedVariableList`

Required evidence per attempt:

- exact ordered member references;
- member count;
- encoded Define request byte count;
- negotiated MMS max PDU when decoded;
- invoke ID;
- Define request/response HEX;
- association state before/after;
- receive-routing evidence;
- exact GetAttributes returned member count/order;
- directory response HEX;
- Delete request/response evidence;
- cleanup success/failure.

The application-side member ceiling is a safety bound only. It must never be reported as the relay's capability.

## G2.2 — qualification ladder and failure localization

Target ladder starts conservatively at:

`1 -> 4 -> 8 -> 16 -> 32`

The ladder is evidence-driven and may stop below or extend above these milestones only through an explicit application safety policy.

When a batch fails while the association survives and cleanup succeeds, split the failed exact member set rather than guessing:

- 8 -> 4 + 4
- failing 4 -> 2 + 2
- failing 2 -> 1 + 1

This distinguishes likely member-specific failure from count/PDU/composition limits.

If the association is lost or cleanup is not proven, do not continue the ladder on that association. A fresh association is required before the next explicit commissioning attempt.

Preferred execution uses an auxiliary MMS association when the IED permits multiple associations. If the device permits only one association, qualification is an explicit commissioning operation and must not run automatically during normal startup.

## G2.3 — persisted qualification profile and smart planner input

Persist evidence under a stable IED fingerprint/model/firmware/profile identity.

Minimum profile content:

- dynamic NamedVariableList support actually proven;
- largest safe member count;
- largest safe encoded Define request size;
- negotiated max PDU observed during qualification;
- known bad/incompatible members, if isolated;
- proven free dynamic BRCB/URCB capacity;
- RCB activation proof state;
- GI InformationReport proof state;
- dchg InformationReport proof state;
- profile creation/update timestamp and source evidence identity.

Meaningful IED identity/firmware/profile changes invalidate production eligibility and require requalification.

Planner sizing must be evidence-driven:

`effectiveMembersPerReport = min(applicationCeiling, provenIedSafeMemberCount, encodedPduBudget)`

`effectiveDynamicPlanCount = min(requiredGroups, provenFreeRcbSlots, applicationSafetyCeiling)`

Existing defaults such as 64 members/report and 8 dynamic plans may remain application ceilings, but they are not the IED capability model.

## G2.4 — one-RCB activation proof

Activate exactly one small dynamic URCB first unless device evidence explicitly requires another safe type.

Candidate members should initially be simple, exact scalar points with:

- one literal MMS reference;
- successful direct MMS read;
- known report projector behavior;
- no current raw/ambiguous structured report payload.

Success requires all of the following:

1. qualified DataSet definition;
2. exact DataSet read-back;
3. exact fresh RCB revalidation;
4. safe RCB binding/configuration;
5. RptEna accepted;
6. association remains healthy;
7. actual InformationReport received;
8. report identity/member mapping verified;
9. at least one selected process value becomes report-authoritative only after valid report evidence.

If any gate fails, retain MMS polling authority and rollback best-effort.

## G2.5 — controlled scale-out

Scale only after one-RCB InformationReport proof:

`1 dynamic group -> 2 groups -> N groups`

At every scale step:

- re-read fresh RCB availability;
- stay inside the persisted safe member/PDU envelope;
- stay inside proven free RCB capacity;
- keep static coverage untouched;
- preserve polling for every not-yet-proven point;
- stop scale-out after the first real dynamic failure and preserve exact failure evidence.

The first practical target for the current field shape is report-backing the 129 exact-mapped selected points while leaving the five unresolved exact-mapping points on polling. Fuzzy matching is not an acceptance criterion.

## G2.6 — full field regression and production eligibility

G2 is complete only after physical field evidence confirms:

- G1 control regression remains green across repeated Open/Close cycles;
- static BRCB/URCB reporting remains intact;
- dynamic report-backed points deliver real reports without destabilizing the association;
- polling is reduced only for points whose report authority is proven;
- reconnect/re-arm behavior remains bounded and recoverable;
- IEC quality provenance remains unchanged;
- no dynamic write loop occurs after a real failure;
- the production planner consumes only a valid persisted `ProductionEligible` profile.

Only after these gates are complete may automatic dynamic activation be removed from the P6.2-B quarantine for a qualified device profile.

## Separate report-fidelity backlog

The following field observations are important but intentionally separated from early G2 qualification so transport/qualification root cause remains clear:

- buffered report overflow;
- MMS-observed data changes not delivered by an armed static report;
- vendor/raw structured report payloads that remain fail-closed;
- structured Boolean status projection rejection.

They should be addressed as report-fidelity work without weakening the G2 qualification gates.
