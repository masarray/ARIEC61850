# Live Discovery to Generic SCL Generation Strategy

ARIEC61850 generates **generic IID/CID-style SCL** from live IEC 61850/MMS discovery. The generated file is intended to be importable by ARIEC61850 and reusable for client workflows such as report selection, DataSet mapping, GOOSE/SV profile creation, and later SCL-backed simulation.

## Design boundary

The generated SCL is not the vendor's original ICD. Vendor engineering templates, private sections, original type IDs, inactive capabilities, and system-wide Substation bindings are not guaranteed to be available through online MMS discovery.

The exporter therefore writes:

- exact evidence where the IED exposes it online, such as LD/LN names, FC paths, DataSet membership, RCB references, RptID, DatSet, and ConfRev;
- generated generic `DataTypeTemplates` from FC, DA paths, MMS type evidence, and CDC inference;
- companion JSON reports for uncertainty, warnings, runtime states, and discovery evidence.

Runtime-only fields such as `RptEna`, `ResvTms`, ownership state, `EntryID`, `SqNum`, and RCB contention are deliberately excluded from static SCL configuration.

## Pipeline

```text
MMS association
  -> GetNameList / FC directory
  -> DataSet directory
  -> RCB inventory
  -> GetVariableAccessAttributes sampling
  -> canonical live IED model
  -> generic DataTypeTemplates
  -> generic IID/CID-style SCL
  -> ARIEC61850 round-trip parse
  -> report/GOOSE/SV/simulator workflows
```

## Export profiles

### connection

Minimal importable file for client connection and reporting workflows:

- `Header`
- `Communication/SubNetwork/ConnectedAP/Address`
- `IED/AccessPoint/Server/LDevice/LN0/LN`
- `DataSet`
- `ReportControl`
- generic `DataTypeTemplates`

### model

Future profile that expands typed model coverage with stronger `DOType`, `DAType`, and `EnumType` generation.

### simulation

Future profile that keeps the same generic model but adds simulator-friendly defaults and explicit behavior profiles for BRCB/URCB/GOOSE/SV.

## CLI

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-scl-export 192.16.1.157 --port 102 --timeout-ms 120000 --max-report-probes 286 --read-datasets true --read-types true --max-type-reads 512 --type-read-source both --ied-name OCR7SR12 --ap-name AP1 --profile connection --output out/scl/OCR7SR12.generated.iid
```

The command performs read-only discovery and writes:

- `*.generated.iid`
- `*.scl-export-report.json`
- `*.scl-export-summary.md`
- optional `discovery-evidence/` bundle next to the generated SCL

## Importability rules

The exporter must ensure:

- every LN `lnType` has a generated `LNodeType`;
- every `DO type` has a generated `DOType`;
- every structured `DA`/`BDA` has a generated `DAType`;
- every DataSet `FCDA` is converted from live reference to `ldInst/prefix/lnClass/lnInst/doName/daName/fc`;
- every `ReportControl` references a local DataSet name when known;
- generated type IDs are safe generic IDs, not vendor-original IDs.

## Current phase

N5.3/N5.5 v1 implements a connection-profile writer with generic `DataTypeTemplates`, `DataSet`, and `ReportControl` export. The CDC registry now rejects internal labels such as `GEN`, `Status`, `Controllable`, `Setting`, and `Measurement`; generated `DOType cdc` values must be known IEC 61850 CDC names or the uncertain DO is omitted with a warning. Live-discovered concrete RCB names such as `brcbA01` are exported with `indexed="false"` so engineering tools do not append another instance suffix and probe invalid names such as `brcbA0101`. GOOSE/SV/SGCB/LCB sections now have structured discovery inventory and SCL shell export; exact DatSet, IDs, APPID/MAC/VLAN, and timing values still require the next online value-reader/passive enrichment phase.

## N5.6 Full SCL Discovery Inventory v1

Edition 1 export is intentionally deferred. The current priority is a richer Edition 2 / Edition 2.1-ready discovery model that can later feed strict edition profiles.

This phase promotes GO/SV/SG/LG discovery from loose placeholders into a structured control-block inventory:

- `GSEControl` inventory from live `GO` functional-constraint attributes.
- `SampledValueControl` inventory from live `MS` / `US` functional-constraint attributes.
- `SettingControl` inventory from live `SG` / `SE` attributes and relay variants such as `LLN0.SP.SGCB`.
- `LogControl` inventory from live `LG` attributes.
- Export evidence file: `control-block-inventory.json`.
- Backward-compatible evidence file: `control-block-placeholders.json`.

The exporter can now emit conservative SCL control-block shells for discovered GOOSE/SV/setting/log controls. It does **not** invent multicast address, APPID, VLAN, GoID, svID, DatSet, or timing values when they have not been read. Missing control-block values are reported as warnings and kept in companion JSON.

Next phase: online control-block value reader for GoCB/SVCB/SGCB/LCB attributes, then passive GOOSE/SV enrichment for multicast address binding.
