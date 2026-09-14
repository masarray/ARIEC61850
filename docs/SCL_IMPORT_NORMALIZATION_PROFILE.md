# SCL Open / Import Normalization Profile

> **READ THIS BEFORE changing Open SCL, SCL parsing, canonical-model construction, edition conversion, Save SCL, SCL-assisted connect, or source-document preservation.**

This document extends the live-MMS-to-SCL reconstruction contract with a second ingestion path: **Open an existing SCL file, normalize it into the same generic semantic model, then export that model through the same edition profiles used by live discovery.**

The goal is deterministic, vendor-neutral interoperability. External-tool observations are used only as black-box protocol/workflow evidence consistent with [`../AGENTS.md`](../AGENTS.md).

Related documents:

- [`../SCL_EXPORT.md`](../SCL_EXPORT.md) — 30-second import/export contract.
- [`SCL_EXPORT_RECONSTRUCTION_PROFILE.md`](SCL_EXPORT_RECONSTRUCTION_PROFILE.md) — live MMS -> canonical model -> multi-edition export.
- [`SCL_ASSISTED_MMS_CONNECT_STEP1.md`](SCL_ASSISTED_MMS_CONNECT_STEP1.md), [`SCL_ASSISTED_MMS_CONNECT_STEP2.md`](SCL_ASSISTED_MMS_CONNECT_STEP2.md), and current Step 3 work — typed SCL-assisted online association.
- [`../RCB_REPORTING.md`](../RCB_REPORTING.md) — DataSet/RCB/reporting semantics.
- [`../AGENTS.md`](../AGENTS.md) — provenance and engineering rules.

---

## 1. New interoperability observation

A controlled engineering-client workflow was observed to support both:

```text
A. Discover live IED -> Save SCL -> choose target edition
B. Open existing SCL -> Save SCL -> choose the same target editions
```

The SCL produced from path B was reported to be semantically the same or very similar in structure/style to the SCL produced from path A.

This strongly supports a **generic internal semantic model plus edition-specific projection**. It does not yet prove byte identity or perfect preservation of every private/vendor field.

Architecture conclusion:

```text
LIVE DISCOVERY -----------+
                           |
                           v
                    CANONICAL MODEL
                           ^
                           |
OPEN SCL -> parse/normalize+
                           |
                           v
                 SAME EXPORT PROFILES
```

**Do not create separate semantic/export engines for live-origin and file-origin models.**

---

## 2. Required architecture

```text
                     INPUT PATH A
                   LIVE MMS ENDPOINT
                         |
                  discovery/evidence
                         |
                         v
                  LiveEvidenceAdapter
                         |
                         |
                         v
+---------------------------------------------------+
|             CANONICAL IEC 61850 MODEL             |
| identity                                          |
| communication                                     |
| LD / LN / DO / DA semantic tree                   |
| canonical basic types / FC / CDC semantics        |
| DataSets / RCB declarations                       |
| declared configuration                            |
| optional live runtime snapshot                    |
| provenance / confidence / diagnostics             |
+---------------------------------------------------+
                         ^
                         |
                  SclImportNormalizer
                         ^
                         |
                    SCL XML parser
                         |
                   EXISTING SCL FILE
                     INPUT PATH B

                         |
                         v
                  SCL EXPORT PROFILES
                         |
        +----------------+----------------+
        v                v                v
    Ed2 V3.1         Ed1 V1.6        Ed1 V1.5/V1.4
```

The canonical model is the engine boundary. XML and MMS wire structures are evidence/input representations, not long-term sources of truth.

---

## 3. Open SCL is semantic import, not DOM round-tripping

Do not implement Open SCL as:

```text
XML DOM -> application state -> string edits -> XML DOM save
```

Instead:

```text
XML
 -> detect source edition/profile
 -> parse typed SCL semantics
 -> resolve references
 -> normalize edition-specific representations
 -> build canonical semantic model
 -> retain source-only evidence separately
```

After canonicalization, the same exporter used for discovery-origin models must be used.

---

## 4. Source-edition import profiles

Import needs typed profiles corresponding to supported source schemas:

```text
SclImportProfile
    Edition2Schema31
    Edition1Schema16
    Edition1Schema15
    Edition1Schema14
```

Profiles decode source representation into canonical semantics, for example:

```text
Ed2 ObjRef / older compatible string -> Canonical ObjectReference
Ed2 EntryID / older compatible Octet8 -> Canonical EntryId
Ed2 SE / older compatible SG          -> Canonical SettingEdit semantic
explicit/unsupported trigger fields    -> canonical state + provenance
```

Never normalize through global XML text replacement.

---

## 5. Unknown is not false

Older editions may be unable to represent a semantic field available in newer profiles. Reopening an old file must not invent the missing value.

Use explicit semantic state such as:

```text
KnownTrue
KnownFalse
Unknown
NotRepresentableInSourceProfile
DefaultedByStandardProfile   // only when independently justified
```

Cross-edition round trip can be lossy; loss must be explicit in diagnostics.

---

## 6. Normalized interoperable output is distinct from source preservation

Primary mode:

```text
NormalizedInteroperable export
    canonical semantics authoritative
    deterministic generic ordering/IDs/style
    target profile controls representation
```

Optional future mode:

```text
SourcePreserving export
    tries to retain lexical/private source details
    never becomes the canonical semantic model
```

Do not promise byte-for-byte round trip without a dedicated source-preserving mode and tests.

---

## 7. Preserve source evidence separately

Opened SCL may contain information unavailable from live MMS:

- original type IDs;
- descriptions;
- Header/history metadata;
- Substation topology;
- private/vendor XML extensions;
- tool-specific metadata;
- original ordering/lexical choices.

Keep a separate source envelope conceptually:

```text
SclSourceEvidence
    sourceEdition
    sourceFileIdentity
    originalTypeIdAliases
    descriptions
    headerMetadata
    topologyMetadata
    opaqueExtensions
    unknownElements
    diagnostics
```

A normalized export may omit unsupported source-only material, but must report that omission.

---

## 8. Type identity rule

For live discovery, type IDs may need deterministic synthesis.

For Open SCL, original IDs are observable and should be preserved as **source aliases**, not treated as semantic identity.

```text
CanonicalTypeIdentity
    semanticFingerprint
    canonicalStableId
    sourceAliases[]
```

This lets discovery and imported files converge on the same type semantics even when their lexical IDs differ.

---

## 9. IED identity by ingress path

### Live discovery
Use the evidence-scored resolver in `SCL_EXPORT_RECONSTRUCTION_PROFILE.md`.

### Open SCL
`IED@name` is authoritative for that file. Record source/provenance explicitly.

### Open SCL + live connect
Cross-check file identity against live domains/endpoint evidence. A mismatch must fail or diagnose explicitly; never silently rename the imported IED.

---

## 10. Three workflows, one model

```text
1. DISCOVER LIVE
   wire evidence -> canonical structural model + runtime snapshot

2. OPEN SCL OFFLINE
   parsed SCL -> canonical structural/configuration model

3. OPEN SCL + CONNECT
   parsed SCL -> canonical structural model
              + live validation evidence
              + live runtime overlay
```

Do not create a duplicated connected-SCL semantic tree. Keep layers distinct:

```text
CanonicalStructuralModel
DeclaredConfiguration
LiveValidationEvidence
LiveRuntimeSnapshot
```

---

## 11. Declared configuration is not runtime state

An SCL file can declare DataSets, RCBs, controls, services and communication settings. It does not prove current online values such as `RptEna`, Owner/Resv, current EntryID, buffer contents, runtime-added DataSets, current process values, or association state.

Offline import must never fabricate live evidence.

---

## 12. One exporter for every origin

After canonicalization:

```text
CanonicalIedModel
    -> SclSemanticModelBuilder
    -> SclEditionCompatibilityTransformer
    -> SclXmlSerializer
    -> SclSchemaValidator
    -> conversion diagnostics/evidence
```

Origin belongs in provenance, not in the serializer's branching logic.

---

## 13. Semantic idempotence is the round-trip target

Same-edition normalized round trip:

```text
Open SCL
 -> canonicalize
 -> save same edition
 -> reopen
 -> canonicalize

semantic fingerprint A == semantic fingerprint B
```

Byte equality is not required.

---

## 14. Cross-ingress equivalence target

```text
Path A:
Discover live -> Canonical A

Path B:
Export normalized SCL from A -> Open -> Canonical B

Expected:
representableSemanticFingerprint(A)
    ==
representableSemanticFingerprint(B)
```

Exclude provenance and live-only runtime fields.

This is the strongest proof that discovery and Open SCL use one semantic model.

---

## 15. Cross-edition equivalence target

For target profile X:

```text
Canonical A
 -> export X
 -> import X
 -> Canonical B
```

Required:

```text
RepresentableSemanticSubset(A, X)
    ==
RepresentableSemanticSubset(B, X)
```

Every dropped/unrepresentable feature must appear in a conversion-loss report.

---

## 16. Determinism

The same canonical model and profile must yield the same normalized output regardless of:

- worker scheduling;
- UI state;
- dictionary/hash iteration order;
- equivalent input enumeration order;
- unrelated wall-clock timing.

A worker/task can improve responsiveness; it cannot determine semantics.

---

## 17. Suggested engine components

Reuse existing ARIEC61850 types where possible; names are illustrative:

```text
SclEditionDetector
SclImportProfile
SclSemanticImporter
SclReferenceResolver
SclImportNormalizer
SclSourceEvidence
CanonicalIedModel
CanonicalModelFingerprint
CanonicalEvidenceReconciler
LiveRuntimeOverlay
SclExportProfile
SclEditionCompatibilityTransformer
SclXmlSerializer
SclSchemaValidator
SclConversionLossReport
```

Do not create parallel models merely to match this list.

---

## 18. Recommended implementation order

1. **Audit the canonical boundary.** Reuse current typed SCL/live models rather than creating another tree.
2. **Add edition-aware import normalization.** Parse each supported source edition into canonical semantics.
3. **Add semantic fingerprinting.** Ignore lexical trivia and provenance while comparing semantics.
4. **Reuse one target exporter.** Discovery and Open SCL must converge before serialization.
5. **Add conversion-loss diagnostics.** Never silently invent or discard semantics.
6. **Integrate SCL-assisted live validation.** Reuse current association planning/runtime; overlay live evidence without mutating imported truth silently.
7. **Add UI only after engine tests pass.** Open/Save dialogs remain thin application surfaces.

---

## 19. Regression targets

Use project-owned synthetic fixtures:

```text
Open Ed2 -> save Ed2 -> reopen -> semantic idempotence
Open Ed1.6 -> save Ed2 -> unknown/loss semantics explicit
Open Ed2 -> save Ed1.4 -> reopen -> representable subset stable
synthetic discovered model -> save -> reopen -> same semantic subset
same canonical model -> all supported target editions
SCL IED@name remains authoritative-for-file
SCL/live identity mismatch fails or diagnoses explicitly
private extension retained as source evidence or reported dropped
source type IDs retained as aliases but not semantic equality keys
offline Open SCL never fabricates runtime RCB state
threaded and synchronous normalization yield identical fingerprints
```

---

## 20. Acceptance invariants

Before merging Open-SCL/Save-SCL changes:

1. Live discovery and SCL import converge on one canonical semantic model.
2. Open SCL parses typed semantics; XML DOM is not the protocol source of truth.
3. Source edition is handled by a typed import profile.
4. Target edition uses the same export profiles as discovery-origin models.
5. Unknown/not-representable values are never silently converted to false/default.
6. Source-only metadata is preserved separately or explicitly reported when dropped.
7. Original type IDs are aliases/provenance, not semantic identity.
8. `IED@name` is authoritative for the input file and is validated, not silently rewritten, online.
9. Declared configuration is distinct from live runtime evidence.
10. Save SCL performs no network I/O.
11. Normalization is deterministic.
12. Same-edition round trip uses semantic equivalence, not byte equality.
13. Cross-edition comparison uses only representable semantics.
14. Conversion loss is explicit.
15. Thread scheduling cannot alter output semantics.
16. Export logic does not fork by model origin after canonicalization.
17. Existing reporting and production qualification rules remain stronger than SCL conversion convenience.
18. Fixtures remain project-owned/provenance compliant.

---

## 21. Evidence boundary / still unproven

Observed workflow evidence supports:

```text
Open SCL
 -> same multi-edition Save SCL choices
 -> normalized output similar to discovery-derived output
```

Still to prove through controlled project tests:

- exact semantic diff from source SCL to normalized output by edition;
- private-extension preservation/drop policy;
- exact ordering and canonical type-ID policy after import;
- full semantic equality between Open-SCL output and discovery output for the same IED;
- upgrade/downgrade loss behavior for semantics unavailable in older profiles.

These are test targets, not reasons to fork the architecture.

---

## 22. Final mental model

```text
              LIVE DISCOVERY
                    |
                    v
             typed wire evidence
                    |
                    +-------------------+
                                        |
                                        v
                              CANONICAL IED MODEL
                                        ^
                                        |
                    +-------------------+
                    |
                    v
              SCL IMPORT/NORMALIZE
                    ^
                    |
                 OPEN SCL

CANONICAL IED MODEL
        |
        +--> live validation/runtime overlay when connected
        |
        +--> Edition 2 export
        +--> Edition 1.6 export
        +--> Edition 1.5 export
        `--> Edition 1.4 export
```

**One model. Multiple ingress paths. Multiple export profiles. No semantic duplication.**
