# Live IED Model Discovery Strategy

ARIEC61850's next product-grade target is to convert a live IEC 61850 IED into a canonical model that can later be exported as a generic IID/CID-style SCL file and used as a simulator seed.

This is not a promise to recreate the vendor's original ICD. The live path reconstructs an importable and auditable model from what the IED exposes online through IEC 61850/MMS services and from passive process-bus enrichment when available.

## Discovery doctrine

1. **Facts stay facts.** Logical devices, logical nodes, MMS `$FC$` paths, DataSet members, RCB attributes, and readback values are stored as observed facts.
2. **Inference is labelled.** CDC, DOType, DAType, EnumType, and SCL template IDs are reconstructed with confidence scoring and evidence.
3. **Runtime state is not static SCL.** `RptEna`, `ResvTms`, `EntryID`, `SqNum`, ownership, contention, and claim failures belong in evidence JSON, not in the generated SCL configuration.
4. **Generated SCL must be round-trip validated.** The exporter must generate references that ARIEC61850 can re-import and use to connect, browse, configure report sessions, and seed a simulator.

## Phase 1 implementation

`mms-model-discover` is the first implementation phase. It performs read-only discovery, builds a canonical model, infers CDC at confidence levels, and writes a discovery evidence bundle.

```powershell
 dotnet run --project .\apps\AR.Iec61850.Cli -- mms-model-discover 192.16.1.157 --port 102 --timeout-ms 120000 --max-report-probes 286 --read-datasets true --ied-name OCR7SR12 --ap-name AP1 --output out/ied-model-discovery
```

Output files:

- `ied-model.json` — canonical live IED model.
- `discovery-summary.md` — human-readable coverage, FC counts, DataSets, RCBs, and CDC inference snapshot.
- `type-confidence-report.json` — CDC/template confidence evidence per DataObject.
- `datasets.json` — DataSet inventory and members when read.
- `rcb-inventory.json` — BRCB/URCB inventory from live RCB discovery.
- `control-block-inventory.json` — structured GO/SV/SG/LG control block inventory detected from live FC attribute names, with value-read gaps explicitly marked.

## Canonical model layers

```text
Live MMS GetNameList / GetNamedVariableListAttributes / RCB Readback
  -> FC resolved points
  -> LogicalDevice / LogicalNode / DataObject / DataAttribute tree
  -> DataSet inventory + reverse RCB usage
  -> RCB inventory
  -> GO/SV/SG/LG placeholder inventory
  -> CDC inference + confidence report
  -> Generic type template candidates
  -> SCL exporter input
```

## FC discovery

Functional Constraint is treated as exact when it is derived from:

- MMS variable name segments such as `LN$ST$DO$DA`.
- DataSet member references.
- future GetDataDirectoryFC-style services.
- successful smart-read candidate resolution.

## CDC inference

CDC is inferred, not blindly claimed. The first rule set covers field-proven patterns:

| Pattern | Inferred CDC | Confidence |
| --- | --- | ---: |
| `NamPlt` | `LPL` | high |
| `PhyNam` | `DPL` | high |
| `Beh`, `Health`, `AutoRecSt`, `OpTmh` | `INS` | high |
| `Mod` | `INC` | high |
| protection operation DO `Op` / `Tr` with activation attributes | `ACT` | high |
| `Str` with general/directional start pattern | `ACD` | high |
| generic `stVal/q/t` | `SPS` | medium |
| `SPCSO*` / single-point control pattern | `SPC` | medium |
| `Pos` / double-point control pattern | `DPC` | medium |
| `mag/q/t` analogue measurement pattern | `MV` | medium |
| phase structures under `MMXU` | `WYE` | medium |
| phase-to-phase structures | `DEL` | medium |
| counter pattern with `actVal`/`pulsQty` | `BCR` | medium |
| sequence pattern | `SEQ` | medium |
| CO/Oper/SBOw/control pattern without exact metadata | `SPC` | low |
| SP/SG/SE/setVal pattern without exact metadata | `SPG` | low |
| unresolved structure | omitted from generated DataTypeTemplates with warning | unknown |

The exporter rejects internal semantic labels such as `GEN`, `Status`, `Controllable`, `Setting`, and `Measurement` as SCL `cdc` values. Later phases will add a pluggable NSD/profile registry and stronger GetVariableAccessAttributes-backed type reconstruction.

## Live-to-SCL dependency chain

The generated SCL exporter must be implemented after these pieces are stable:

1. Full FC/DA tree from live discovery.
2. MMS type reader / GetVariableAccessAttributes equivalent.
3. CDC inference confidence report.
4. Generic DataTypeTemplates builder.
5. Communication/ConnectedAP writer.
6. DataSet/ReportControl/GSEControl/SMV writer.
7. Round-trip validator.
8. SCL-backed server/simulator.

## Next sub-milestones

- N5.2 — MMS variable access type reader.
- N5.3 — full CDC pattern registry and confidence scoring.
- N5.4 — generic DataTypeTemplates builder.
- N5.5 — IID/CID writer with Communication, IED, DataSet, RCB, and minimal templates.
- N5.6 — SCL round-trip import and report connection from generated SCL.
- N5.7 — simulator seed from generated SCL.

## N5.2 MMS VariableAccessAttributes Type Reader

Live model discovery now has an optional type-reading pass. The pass calls MMS `GetVariableAccessAttributes` for a bounded set of candidate variables, then attaches the decoded MMS `TypeSpecification` to the canonical model.

This is intentionally different from claiming that the original vendor SCL `DataTypeTemplates` were recovered. Online MMS type discovery provides operational type evidence: structure, array/basic type, approximate SCL `bType`, and a generated signature. CDC and SCL template IDs remain reconstructed with confidence scoring.

Recommended command:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-model-discover 192.16.1.157 --port 102 --timeout-ms 120000 --max-report-probes 286 --read-datasets true --read-types true --max-type-reads 256 --type-read-source both --ied-name OCR7SR12 --ap-name AP1 --output out/ied-model-discovery
```

Output added by this phase:

- `variable-access-attributes.json` - raw per-variable MMS type discovery result.
- `ied-model.json` - each data attribute now contains `MmsType`, `MmsTypeSignature`, `TypeDiscoveryStatus`, `TypeSource`, and `TypeConfidence`.
- `type-confidence-report.json` - includes MMS type evidence beside FC and CDC inference.
- `discovery-summary.md` - shows type-read coverage and a type snapshot table.

Type read source modes:

- `datasets` - read types only for discovered DataSet members. This is safest and fastest.
- `model` - sample from the discovered FC point model, prioritized by ST/MX/value-bearing attributes.
- `both` - DataSet members first, then model samples until `--max-type-reads` is reached.

The next phase, N5.3/N5.4, will use these signatures to generate stronger generic `DataTypeTemplates` for IID/CID export.
