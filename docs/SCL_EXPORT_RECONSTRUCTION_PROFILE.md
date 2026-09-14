# SCL Export Reconstruction Profile

> **READ THIS BEFORE changing live IED identity resolution, live-model reconstruction, SCL type synthesis, Save/Export SCL, or Edition 2 / Edition 1 compatibility code.**

This document records the vendor-neutral protocol facts and architecture conclusions derived from controlled live MMS discovery plus multi-version SCL export observations. It is written for future human and AI development threads so the exporter is not repeatedly re-invented from partial memory.

The purpose is **not** to clone a commercial implementation. The purpose is to preserve minimum interoperability facts, reconstruct them independently, and turn them into deterministic project-owned behavior consistent with [`../AGENTS.md`](../AGENTS.md).

Raw external-client captures and product-specific UI details are intentionally not part of the repository. Capture-derived values below are labelled as evidence and must not be hard-coded as universal IEC 61850 rules.

Related documents:

- [`../SCL_EXPORT.md`](../SCL_EXPORT.md) — 30-second entry point.
- [`SCL_ASSISTED_MMS_CONNECT_STEP1.md`](SCL_ASSISTED_MMS_CONNECT_STEP1.md) — typed SCL communication context.
- [`SCL_ASSISTED_MMS_CONNECT_STEP2.md`](SCL_ASSISTED_MMS_CONNECT_STEP2.md) — typed association-plan/encoder path.
- [`../RCB_REPORTING.md`](../RCB_REPORTING.md) — DataSet/RCB/reporting semantics relevant to live-state export.
- [`G2_EVIDENCE_QUALIFIED_DYNAMIC_REPORTING.md`](G2_EVIDENCE_QUALIFIED_DYNAMIC_REPORTING.md) — stronger production qualification gates for dynamic reporting.
- [`../AGENTS.md`](../AGENTS.md) — provenance and engineering discipline.

---

## 1. The mental model to remember

The observed behavior is best explained by **one live discovery and one canonical semantic model**, followed by local edition-specific export.

```text
                      LIVE MMS ENDPOINT
                              |
                              v
                     Discovery / evidence
              domains, variables, types, Reads,
                    DataSets, RCB state
                              |
                              v
                  CANONICAL LIVE IED MODEL
          identity + communication + LD/LN/DO/DA/types
                  + DataSets + RCB runtime state
                              |
                              v
                       CACHE LOCALLY
                              |
          +-------------------+-------------------+
          |                   |                   |
          v                   v                   v
   Edition 2 V3.1      Edition 1 V1.6      Edition 1 V1.5
          |                   |                   |
          |                   +-------------------+
          |                                       |
          |                                  Edition 1 V1.4
          v                                       |
   typed compatibility/profile transformation     |
          +-------------------+-------------------+
                              |
                              v
                   schema-specific serializer
                              |
                     IID / ICD output
```

**Do not implement one discovery algorithm per schema.**

**Do not make Save SCL perform hidden rediscovery merely because the user selects another output edition.**

**Do not use the output XML itself as the canonical internal model.**

---

## 2. Controlled evidence: discovery happened once, export stayed local

In the controlled session used for this analysis, meaningful MMS discovery traffic completed early in the association. The association then remained open for a much longer interval with only TCP keepalive activity while multiple SCL schema variants were saved.

No additional confirmed MMS discovery service was observed during the save interval.

Engineering conclusion:

```text
Save SCL
    !=
rediscover IED

Save SCL
    =
project cached canonical model through selected export profile
```

This is an observed architecture clue, not a requirement that every engineering client keep the association open while saving.

ARIEC61850 implication:

- discovery/network I/O and SCL serialization are separate layers;
- the same model snapshot can be exported repeatedly without network access;
- selecting another schema must not alter the association or live state;
- exporter tests should operate entirely from a project-owned canonical model fixture.

---

## 3. Controlled discovery evidence used by the export

One controlled live-discovery run contained **419 confirmed MMS transactions** with strict sequential invoke IDs.

Observed service counts:

```text
GetNameList                         140
GetVariableAccessAttributes        119
Read                               157
GetNamedVariableListAttributes       3
--------------------------------------
confirmed transactions             419
```

The three NamedVariableList attribute reads corresponded to the three DataSets visible in that live snapshot: two previously present DataSets plus one dynamic DataSet created during an earlier reporting experiment.

The resulting SCL export also contained **three DataSets** and reflected the current RCB binding to that dynamic DataSet.

Engineering conclusion:

> Live-to-SCL reconstruction represents the **current discovered MMS model**, including mutable runtime DataSet/RCB evidence when it is part of the snapshot.

Therefore keep structural model identity distinct from runtime DataSet/RCB state.

---

## 4. Canonical semantic structure was stable across all four exports

The four exports from one discovered model had the same semantic object counts:

```text
IED               1
LDevice          32
LN0              32
LN               87
DataSet           3
FCDA             71
ReportControl    32

LNodeType        38
DOType           60
DAType           17
EnumType         19
```

This strongly supports:

```text
one canonical model
    -> several schema projections
```

These counts are capture-specific regression evidence only; never encode them as protocol limits.

---

## 5. IED-name resolution: what the evidence supports

### 5.1 Observation

The live `GetNameList(Domain, VMD)` response returned 32 MMS domains. All domains shared one stable leading naming component, and removing that component produced the Logical Device instance portion used in the reconstructed SCL.

No dedicated MMS request containing an explicit `IEDName` field was observed.

The exported SCL nevertheless used one precise IED identity consistently in both `IED@name` and `ConnectedAP@iedName`.

### 5.2 Required architecture

Treat IED identity as a typed **evidence-scored resolution result**, not as a UI-thread/worker side effect and not as a blind `longestCommonPrefix()` operation.

A background worker may make discovery responsive, but the same evidence processed synchronously must yield the same identity result.

Naive prefix logic fails for ambiguous names such as:

```text
MYIEDLD1
MYIEDLD2
MYIEDLD3
```

where `MYIEDLD` is a string prefix but is not necessarily the intended IED name.

### 5.3 Recommended resolver

```text
MMS domain directory
       |
       v
generate plausible IED/LD split candidates
       |
       v
score each candidate using independent evidence
       |
       +-- all domains decompose consistently
       +-- resulting LD instances are valid/non-empty
       +-- RptID / DatSet references agree
       +-- absolute object references agree
       +-- reconstructed LN/DO paths remain consistent
       +-- communication/model references do not contradict it
       |
       v
IedIdentityResolution
    value
    source
    confidence
    supportingEvidence
    contradictions
```

Suggested source/confidence semantics:

```text
Trusted SCL IED@name                 -> authoritative-for-that-file
Multi-domain naming consensus        -> high when cross-validated
Single-domain naming inference       -> low/medium unless reinforced
User explicit override               -> explicit source, never hidden inference
Conflicting evidence                 -> ambiguous / unresolved
```

This is consistent with the existing ARIEC61850 rule that resolver results carry source and confidence.

---

## 6. MMS domain -> SCL LDevice decomposition

Once identity is resolved, centralize domain decomposition:

```text
MMS domain
    -> resolved IED identity + LD instance
    -> <LDevice inst="...">
```

Do not repeat string slicing independently in DataSet, RCB, type, control, and export code.

Use one semantic record conceptually equivalent to:

```text
MmsDomainIdentity
    FullDomain
    IedName
    LogicalDeviceInstance
    ResolutionSource
    Confidence
```

---

## 7. LN / FC / DO / DA reconstruction

Live MMS names of the form:

```text
LN$FC$DO$DA...
```

combined with `GetVariableAccessAttributes` / TypeSpecification evidence provide the semantic input for SCL data-model reconstruction.

Architecture:

```text
MMS variable name
       +
GVAA / TypeSpecification tree
       |
       v
canonical LN / DO / DA semantic tree
       |
       v
SCL LNodeType / DOType / DAType / EnumType synthesis
```

Keep XML generation outside the discovery parser.

---

## 8. Type IDs are synthetic, deterministic identities

MMS can reveal semantic structure and live type information, but cannot recover all original engineering-file identities or metadata.

Do not claim live discovery recovers the original vendor:

- `LNodeType@id`, `DOType@id`, `DAType@id` values;
- descriptions/private extensions;
- original enum labels when they are not observable;
- Substation topology;
- original authoring history.

Therefore live export should synthesize stable deterministic type IDs from the semantic tree.

Preserve provenance:

```text
semantic tree         = recovered from live evidence
synthetic type ID     = generated by ARIEC61850
original vendor ID    = unknown / not recoverable
```

---

## 9. DataSet reconstruction

Correct pipeline:

```text
NamedVariableList identity
       |
GetNamedVariableListAttributes
       |
ordered MMS members
       |
resolve through canonical domain/LN/FC/DO/DA model
       |
ordered FCDA[]
       |
SCL DataSet
```

**Preserve exact DataSet member order.**

Do not sort FCDA entries for readability.

This order is also required by report decoding when `DataReference` is absent.

---

## 10. RCB reconstruction and live-state export

RCB semantic projection uses discovered identity plus live state evidence such as:

```text
RP / BR identity
RptID
DatSet
ConfRev
BufTm
TrgOps
OptFlds
IntgPd
buffered/unbuffered mode
```

The controlled evidence showed that a runtime-created DataSet and its current RCB binding appeared in the later SCL export.

Maintain:

```text
structural model != runtime report snapshot
```

This prevents runtime DataSet/RCB changes from being misreported as firmware/model changes.

---

## 11. Edition-profile architecture

Use a typed export profile, conceptually:

```text
SclExportProfile
    Edition2Schema31
    Edition1Schema16
    Edition1Schema15
    Edition1Schema14
```

The profile should own:

- root/version metadata;
- allowed elements/attributes;
- capability representation;
- functional-constraint compatibility;
- basic-type compatibility;
- reference serialization;
- communication-address representation;
- report-control representation;
- default filename extension;
- target schema validation.

Pipeline:

```text
CanonicalSclSemanticModel
       |
       v
EditionCompatibilityTransformer
       |
       v
EditionSpecificSclModel
       |
       v
XmlSerializer
       |
       v
schema validation / diagnostics
```

Do not scatter edition conditionals throughout MMS discovery.

Do not implement downgrade as blind XML search/replace.

---

## 12. Controlled four-way export matrix

Observed export choices:

```text
Edition 2  Schema V3.1 -> IID
Edition 1  Schema V1.6 -> ICD
Edition 1  Schema V1.5 -> ICD
Edition 1  Schema V1.4 -> ICD
```

The extension convention is workflow evidence, not proof that extension alone defines semantic validity.

### 12.1 Edition 1 V1.4 vs V1.5

For this specific model, the full output was effectively identical except for the schema-version comment.

**Never generalize this to all models.** Different features may require real transform/rejection.

### 12.2 Edition 1 V1.5 vs V1.6

For this specific model, observed differences were minimal:

- schema-version metadata/comment changed;
- `OSI-AP-Title` lexical form changed from a quoted older representation to an unquoted V1.6 representation.

This is not a complete universal schema-difference list.

### 12.3 Edition 1 V1.6 vs Edition 2 V3.1

Observed Edition 2 additions/changes included:

```text
SCL root version="2007" revision="B"
MMS-Port present
SGEdit/ConfSG reservation-time capability represented
ConfReportControl buffer-configuration capability represented
ReportSettings owner/reservation-time capability represented
```

Capture-specific transformation counts:

```text
32  TrgOps instances gained explicit GI representation
31  reference values changed from absolute LD-style form to relative @-style form
13  basic types changed from compatible VisString129 form to ObjRef
 7  functional constraints changed from older SG-compatible form to SE
 1  EntryID basic type changed from Octet8-compatible form to EntryID
```

These counts are regression evidence for the captured model only.

---

## 13. Typed compatibility examples

Keep semantic meaning in the canonical model:

```text
CanonicalBasicType.ObjectReference
    Ed2 -> ObjRef
    older target -> compatible VisString129 representation when required

CanonicalBasicType.EntryId
    Ed2 -> EntryID
    older target -> compatible Octet8 representation when required

CanonicalFunctionalConstraint.SettingEdit
    Ed2 -> SE
    older target -> compatible SG representation where required

CanonicalTrigger.GeneralInterrogation
    Ed2 -> explicit gi attribute when supported
    older target -> omit/transform according to target schema
```

If a target schema cannot represent a canonical semantic fact without loss, emit a diagnostic. Do not silently corrupt meaning.

---

## 14. Reference serialization is profile behavior

The same semantic source reference was observed in different lexical forms across editions:

```text
older Edition 1 -> absolute logical-device style
Edition 2       -> @relative style
```

Represent references semantically first, then render according to the target profile.

Do not store only rendered text as the source of truth.

---

## 15. Communication-section reconstruction

Observed export reconstructed communication/association context including:

- IP address;
- TSEL/SSEL/PSEL selectors;
- AP-title;
- AE qualifier;
- MMS port where represented by the profile.

This should consume the same typed communication models used by SCL-assisted MMS connection planning.

Rule:

```text
association/SCL evidence
    -> typed communication model
    -> edition-specific SCL projection
```

The XML serializer must not decode raw COTP/ACSE bytes itself.

---

## 16. Services/capability projection

Capabilities should be semantic typed data, not XML attributes stored as strings.

A profile decides whether each capability is:

```text
representable exactly
representable through compatibility mapping
not representable -> omit with diagnostic
unknown -> do not invent
```

This applies to report settings, setting-group capabilities, buffer configuration, ownership/reservation-time representation, and similar edition-specific details.

---

## 17. Provenance must be first-class

Every exported field should be traceable to one of:

```text
ObservedWire
DerivedFromObservedWire
TrustedSclInput
ProfileCompatibilityTransform
ProfileDefault
SyntheticIdentifier
UserOverride
Unknown
```

At minimum, export evidence should be able to explain:

- why IED name was chosen;
- how MMS domains were decomposed into IED + LD;
- which DataSets were read live and their exact order;
- which RCB values came from live state;
- which type IDs were synthesized;
- which values changed representation because of target edition;
- which semantics could not be represented in the target schema.

---

## 18. Cache and fingerprint rules

Cache the semantic model, not only generated XML.

Recommended separation:

```text
StructuralModel
    LD/LN/DO/DA/type/control identities

RuntimeSnapshot
    current DataSet directory
    current RCB binding/state

CommunicationContext
    endpoint and OSI addressing

IdentityResolution
    IED-name decision + confidence/evidence
```

Do not invalidate structural identity merely because runtime reporting configuration changes.

---

## 19. Suggested implementation components

Names are illustrative:

```text
LiveIedCanonicalModel
IedIdentityResolver
MmsDomainIdentityResolver
LiveTypeTreeBuilder
LiveDataSetProjector
LiveReportControlProjector
SclSemanticModelBuilder
SclTypeIdSynthesizer
SclExportProfile
SclEditionCompatibilityTransformer
SclXmlSerializer
SclSchemaValidator
SclExportEvidence
```

Use existing ARIEC61850 typed models where they already cover these responsibilities; do not create duplicate parallel models merely to match this list.

Core rule:

> One semantic source of truth should feed discovery export, SCL-assisted connection, reporting, evidence, and UI projection.

---

## 20. Worker/threading rule

A worker/task may:

- keep discovery off the UI thread;
- report progress;
- build/serialize/validate asynchronously.

It must not define semantic identity correctness.

Identity resolution should be a pure deterministic operation testable as:

```text
same evidence input
    -> same candidates
    -> same selected identity
    -> same confidence/source
```

---

## 21. Regression fixtures to create independently

Do not commit raw external-client captures as permanent fixtures. Build project-owned synthetic cases.

### Identity

```text
many domains with clean identity
ambiguous common prefix
single-LD IED
similar IED/LD prefixes
contradictory report/DataSet references
explicit user override
trusted-SCL identity
```

### DataSet

```text
exact ordered member preservation
cross-LD FCDA mapping
unknown member diagnostic
runtime-added DataSet represented only in runtime snapshot/export
```

### Type synthesis

```text
stable semantic tree -> stable synthetic IDs
input enumeration-order change does not change semantic identity
unknown engineering metadata remains unknown
```

### Edition profiles

```text
ObjectReference semantic -> Ed2 ObjRef / older compatible representation
EntryId semantic -> Ed2 EntryID / older compatible representation
SettingEdit semantic -> target-specific FC mapping
GI capability -> explicit only where target profile supports it
communication field present/omitted by profile
unrepresentable semantic -> diagnostic
```

### Save behavior

```text
one canonical model -> four deterministic exports
no network transport required during export
switching schema does not mutate canonical model
```

---

## 22. Acceptance invariants

Before merging live-to-SCL work, verify:

1. Discovery and SCL serialization remain separate layers.
2. One canonical semantic model feeds all export profiles.
3. Save/export performs no hidden rediscovery by default.
4. IED-name resolution exposes source/confidence and cross-evidence.
5. Longest-common-prefix is not the sole resolver rule.
6. Domain -> IED/LD decomposition is centralized and typed.
7. DataSet member order is exact.
8. Structural model and runtime DataSet/RCB state are distinct.
9. Synthetic type IDs are never presented as original vendor IDs.
10. Edition conversion is typed semantic transformation, not text replacement.
11. Representation loss/omission produces diagnostics.
12. Same canonical snapshot produces deterministic output.
13. Communication projection consumes typed addressing models.
14. Unobservable engineering metadata is not invented.
15. Capture-specific counts/quirks are not hard-coded.
16. Public fixtures remain project-owned and provenance compliant.
17. Existing G2 reporting qualification remains stronger than export/wire parity.

---

## 23. Known unknowns

Current evidence does not yet prove:

- the exact external-client identity algorithm for single-LD or heavily ambiguous names;
- every schema difference for every V1.4/V1.5/V1.6 feature combination;
- exact downgrade policy for canonical features impossible to represent in an old schema;
- conflict policy between live identity evidence and a user override;
- whether all IEDs expose enough live type information for high-fidelity reconstruction;
- recovery of original vendor type IDs, private metadata, descriptions, or topology from MMS alone.

Keep these explicit. Do not fill them with guesses.

---

## 24. Final architecture reminder

Think in this order:

```text
WIRE FACTS
    -> typed evidence
    -> identity resolution
    -> canonical semantic model
    -> structural/runtime separation
    -> SCL semantic reconstruction
    -> edition compatibility profile
    -> deterministic XML serialization
    -> schema/evidence diagnostics
```

Never reverse that order by starting from one sample XML file and teaching discovery to imitate its strings.
