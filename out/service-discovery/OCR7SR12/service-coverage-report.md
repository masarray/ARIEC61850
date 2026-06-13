# IEC 61850 Online Service Discovery Coverage

- Generated: 2026-06-13 14:20:24.416 UTC
- Target: 192.16.1.157:102
- IED: OCR7SR12

| Service area | Status | Count | Evidence | Remaining gap |
| --- | --- | ---: | --- | --- |
| Data model | Discovered | 9464 | LD=4, LN=123, DO=1186, DA=9464 | - |
| Functional constraints | Discovered | 9464 | FC is extracted from MMS $FC$ names and dataset/readback evidence. | - |
| DataSets | Discovered | 1 | 1 dataset(s) have resolved directory members. | Add deeper DataSet value reads and cross-link to GoCB/SVCB when exposed. |
| Reports / RCB | Discovered | 286 | BRCB=8, URCB=278 | Read all RCB attributes and normalize static SCL state vs runtime state. |
| GOOSE control blocks | Not exposed or not discovered | 0 | Current implementation detects GSEControl attribute inventory when present. | Implement GoCB value reader: GoEna, GoID, DatSet, ConfRev, NdsCom, MinTime, MaxTime, DstAddress. |
| Sampled Value control blocks | Not exposed or not discovered | 0 | Current implementation detects MS/US/SVCB attribute inventory when present. | Implement SVCB value reader: SvID/smvID, DatSet, ConfRev, SmpRate, SmpMod, NofASDU, DstAddress. |
| Setting groups | Core readback complete | 1 | SGCB core readback complete=1/1; SG/SE map entries=0, setting value reads=not attempted. | Map SG/SE setting attributes and add setting value readback evidence. |
| Logs | Not exposed or not discovered | 0 | LogControl inventory is available when LG attributes are exposed. | Implement log directory/query service. |
| File service | Discovered | 0 | FileDirectory returned 0 entries from 1 page(s). | Add FileOpen/FileRead download support and recursive safe directory walking. |
| Variable specifications | Not attempted | 0 | attempted=0, ok=0, failed=0 | Use safe, leaf-only, dataset-first type reads to avoid IED peer-close behavior. |
| Golden SCL type learning | Learning candidates found | 140 | goldenBindings=272, liveDO=1186, unknownOrMedium=459, exactKeyMatches=743, candidates=140, conflicts=2. | Promote confirmed golden CDC/type candidates into the standard/vendor registry and SCL normalizer. |
| Golden registry promotion | Promotions generated + conflicts for review | 67 | profile=OCR7SR12, policy=review-only, candidates=140, applied=67, conflicts=3, registryEntries=67. | Review CDC conflicts before applying golden overrides; keep conflict policy review-only unless validated against the IED/vendor model. |
| CDC resolution | Partially resolved | 899 | high=727, medium=172, low=0, unknown=287 | Expand IEC 61850-7-3/7-4 registry and feed golden SCL learning results into normalized type generation. |

## Next implementation gaps

- GOOSE control blocks: Implement GoCB value reader: GoEna, GoID, DatSet, ConfRev, NdsCom, MinTime, MaxTime, DstAddress.
- Sampled Value control blocks: Implement SVCB value reader: SvID/smvID, DatSet, ConfRev, SmpRate, SmpMod, NofASDU, DstAddress.
- Setting groups: Map SG/SE setting attributes and add setting value readback evidence.
- Logs: Implement log directory/query service.
- Variable specifications: Use safe, leaf-only, dataset-first type reads to avoid IED peer-close behavior.
- Golden SCL type learning: Promote confirmed golden CDC/type candidates into the standard/vendor registry and SCL normalizer.
- Golden registry promotion: Review CDC conflicts before applying golden overrides; keep conflict policy review-only unless validated against the IED/vendor model.
- CDC resolution: Expand IEC 61850-7-3/7-4 registry and feed golden SCL learning results into normalized type generation.

## Interpretation

This report separates what ARIEC61850 already discovers online from what still needs a dedicated MMS service implementation. It is intentionally stricter than the SCL exporter: a service is not marked complete merely because a placeholder or attribute name was seen in the live model.
