# Live-to-SCL Generic Export Blueprint

ARIEC61850 targets the same field workflow expected from professional IEC 61850 tools: connect to a live IED, discover the model, reconstruct a generic IID/CID-style SCL snapshot, re-import it for client workflows, and use it as a simulator seed.

## Boundaries

The generated SCL is **not** the vendor's original ICD. It is a generated engineering snapshot based on live IEC 61850/MMS evidence. It must be importable, internally consistent, and useful for connection/report/GOOSE/SV/simulator workflows, while keeping uncertain or runtime-only information in companion JSON.

## Pipeline

```text
Live MMS discovery
  -> Canonical IED model
  -> FC/DA/type/CDC confidence report
  -> Generic DataTypeTemplates
  -> IID/CID-style SCL writer
  -> Round-trip SCL validator
  -> Client connection from generated SCL
  -> MMS server/simulator seed
```

## SCL sections to synthesize

- `Header`: generated file identity, tool ID, revision.
- `Communication`: `SubNetwork`, `ConnectedAP`, `Address`, IP and OSI parameters known from successful association.
- `IED`: generated `IED`, `AccessPoint`, `Server`, `LDevice`, `LN0`, `LN` tree.
- `DataSet`: reconstructed from GetNamedVariableListAttributes and FCDA references.
- `ReportControl`: reconstructed from RCB discovery/readback; runtime state excluded.
- `GSEControl` and `SMV`: populated when GoCB/SVCB discovery or passive traffic enrichment is available.
- `SettingControl` and `LogControl`: populated when SGCB/LCB evidence exists.
- `DataTypeTemplates`: generated from live structure plus CDC/type inference.

## Companion evidence

Generated SCL must be accompanied by:

- `ied-model.json`
- `type-confidence-report.json`
- `datasets.json`
- `rcb-inventory.json`
- `control-block-inventory.json`
- future `scl-generation-report.json`

## Importability rules

- Every `lnType` must resolve to a generated `LNodeType`.
- Every `DO type` must resolve to a generated `DOType`.
- Every structured DA/BDA must resolve to a generated `DAType`.
- Every enum must resolve to a generated `EnumType` when enum values are known.
- Every DataSet `FCDA` must target a known LD/LN/DO/DA path.
- Every ReportControl `datSet` must target a generated DataSet in scope.
- GSE/SMV communication entries must point to existing control blocks.

## Simulator seed rules

The simulator must treat generated SCL as the server model source, but it must not simulate vendor-private behavior unless such behavior is explicitly configured. Default simulator behavior should expose LD/LN/DO/DA, DataSets, RCBs, and later GOOSE/SV control blocks using safe generated defaults.

## N5.3/N5.5 implementation notes

The first writer phase is implemented as `mms-scl-export`. It performs live discovery, bounded DataSet and type reads, builds the canonical discovery model, writes a generic connection-profile IID, and immediately round-trip parses the result with the ARIEC61850 SCL parser.

Current writer coverage:

- `Header`
- `Communication/SubNetwork/ConnectedAP/Address`
- `IED/AccessPoint/Server/LDevice/LN0/LN`
- `DataSet` with FCDA entries converted from live references
- `ReportControl` from RCB discovery
- generic `LNodeType`, `DOType`, and nested `DAType` chains
- `*.scl-export-report.json` and `*.scl-export-summary.md`

Still pending:

- GoCB/SVCB online control-block attribute readers
- passive GOOSE/SV enrichment for MAC/APPID/VLAN where MMS data is incomplete
- SettingControl/LogControl deep readers
- full SCL schema validation beyond internal round-trip parse
- SCL-backed MMS server/simulator seed
