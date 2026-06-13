# IEC 61850 Online Service Discovery Coverage

- Generated: 2026-06-13 13:15:24.492 UTC
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
| Setting groups | Inventory | 1 | SG/SE inventory is available when exposed in the MMS directory. | Implement SGCB services/readback: NumOfSG, ActSG, EditSG, CnfEdit plus SG/SE setting attributes. |
| Logs | Not exposed or not discovered | 0 | LogControl inventory is available when LG attributes are exposed. | Implement log directory/query service. |
| File service | Discovered | 0 | FileDirectory returned 0 entries from 1 page(s). | Add FileOpen/FileRead download support and recursive safe directory walking. |
| Variable specifications | Safe probe stopped or unsupported | 0 | attempted=1, ok=0, failed=1, selected=13, skipped=0, stoppedEarly=true. scalar=0, structure=0, selected=13/13. | Reduce max type reads, keep dataset-first leaf-only probing, and avoid the last failed reference class. |
| CDC resolution | Partially resolved | 899 | high=727, medium=172, low=0, unknown=287 | Expand IEC 61850-7-3/7-4 registry and compare against golden IEDScout SCL. |

## Next implementation gaps

- GOOSE control blocks: Implement GoCB value reader: GoEna, GoID, DatSet, ConfRev, NdsCom, MinTime, MaxTime, DstAddress.
- Sampled Value control blocks: Implement SVCB value reader: SvID/smvID, DatSet, ConfRev, SmpRate, SmpMod, NofASDU, DstAddress.
- Setting groups: Implement SGCB services/readback: NumOfSG, ActSG, EditSG, CnfEdit plus SG/SE setting attributes.
- Logs: Implement log directory/query service.
- Variable specifications: Reduce max type reads, keep dataset-first leaf-only probing, and avoid the last failed reference class.
- CDC resolution: Expand IEC 61850-7-3/7-4 registry and compare against golden IEDScout SCL.

## Interpretation

This report separates what ARIEC61850 already discovers online from what still needs a dedicated MMS service implementation. It is intentionally stricter than the SCL exporter: a service is not marked complete merely because a placeholder or attribute name was seen in the live model.
