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

## IEDScout-clean connection profile

Generated SCL now distinguishes between the full live model and a connection-safe export profile.  The `iedscout-connection` profile is the default for `mms-scl-export` because OMICRON IEDScout and many engineering tools will actively read objects described by the imported SCL after connecting to the live IED.

The profile intentionally excludes attributes that are useful for a simulator seed but noisy for a connection file:

- control service parameters under `Oper`, `SBOw`, and `Cancel`;
- control leaves such as `ctlVal`, `ctlNum`, `origin.*`, `Check`, `T`, and `Test` when they belong to control service structures;
- optional measurement/configuration leaves such as `db`, `units.*`, `angRef`, `seqT`, `sboTimeout`, and `stSeld` until they are read-proven;
- low-confidence CDC/type inference results.

Excluded entries are not lost.  They are written to `*.scl-excluded-attributes.json` and remain available in the live discovery evidence bundle.  Use `--scl-export-profile full-model` when a larger model is needed for audit, and `--scl-export-profile simulator-seed` when the generated SCL is intended to seed an ARIEC61850 server/simulator.

This separation is critical: a connection SCL should make IEDScout connect cleanly and avoid read attempts against service parameters, while a simulator SCL must preserve enough structure to implement controls later.

## Standard-discovery profile and enum CDC synthesis

When the goal is to catch up with library-grade online discovery instead of producing the smallest possible engineering-file import, use:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-scl-export 192.16.1.157 --port 102 --ied-name OCR7SR12 --ap-name AP1 --scl-export-profile standard-discovery --ld-name-mode auto --read-datasets true --read-types false --output out/scl/OCR7SR12.standard-discovery.iid
```

`standard-discovery` is an alias for the broad `full-model` export profile. It keeps more discovered online structure than the IEDScout connection-clean profile and is intended for model-audit, library-parity work, and future simulator seeding.

The exporter now treats Ed2 enumerated CDCs such as `ENS`, `ENC`, and `ENG` as SCL enum-backed attributes. Online MMS often exposes their runtime values as integers, but an SCL `DOType cdc="ENS"` should not describe `stVal` as a plain integer leaf. The generated DataTypeTemplates now emit `bType="Enum"` with a generated `EnumType` for standard-discovered objects such as `LLN0.Beh`, `LLN0.Health`, `LPHD.PhyHealth`, and `XCBR.CBOpCap`.

This is still a reconstructed SCL model. It is not the original vendor ICD, and enum labels are conservative engineering labels when the vendor-specific names cannot be read online.

## Standard-discovery versus IEDScout connection companion

A full standard-discovery SCL is intentionally richer than a connection-clean SCL. It may include IEC 61850 control-service structures such as `Oper`, `SBOw`, and `Cancel`, plus optional measurement/configuration leaves such as `db`, `units`, `angRef`, and `seqT`. Those structures are useful for model reconstruction and simulator seed work, but some online clients try to read every leaf as an ordinary MMS value during connect/read-all checks. Many real IEDs reject those service parameters or optional configuration leaves, which can produce client warnings even when the SCL opens without schema/model warnings.

For this reason, `mms-scl-export` now emits a connection companion by default whenever the requested export profile is `standard-discovery` or `full-model`:

```powershell
mms-scl-export 192.16.1.157 --scl-export-profile standard-discovery --output out/scl/OCR7SR12.standard-discovery.iid
```

The main output remains the full standard-discovery model:

```text
out/scl/OCR7SR12.standard-discovery.iid
```

The companion output is filtered for IEDScout online connect/read-all behavior:

```text
out/scl/OCR7SR12.standard-discovery.iedscout-connection.iid
```

Use the full file to audit model coverage and type-template reconstruction. Use the companion file when the objective is to reduce IEDScout online read warnings. Disable companion generation with:

```powershell
--write-connection-companion false
```

## N5.12 Golden-reference diff and service coverage

ARIEC61850 now separates two engineering questions that were previously mixed together:

1. **Does the generated SCL look structurally close to a trusted engineering file?**
2. **Which online IEC 61850 services are actually discovered by the native stack?**

Use `scl-diff` to compare a trusted IID/SCD export from a vendor tool or IEDScout with an ARIEC61850-generated file:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- scl-diff .\samples\scl\OCR7SR12.iid .\out\scl\OCR7SR12.standard-discovery.iid --output .\out\scl-diff\OCR7SR12
```

The command writes:

- `scl-golden-diff-report.md`
- `scl-golden-diff-report.json`
- `missing-services.json`
- `do-cdc-diff.json`
- `type-template-reuse.json`

Use `mms-service-discover` to produce an online coverage bundle without writing to the IED:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-service-discover 192.16.1.157 --port 102 --ied-name OCR7SR12 --read-datasets true --read-types false --output .\out\service-discovery\OCR7SR12
```

The command writes the normal live model evidence plus `service-coverage-report.md/json`, explicitly separating discovered areas from remaining protocol-service gaps such as file service, log service, setting-group service, and GoCB/SVCB value readers.

## Variable specification quarantine and golden type learning

`GetVariableAccessAttributes` can be useful for exact MMS type discovery, but field devices may reject or even close the TCP association for this service. ARIEC61850 therefore treats it as optional and isolated:

- `--type-read-isolated true` opens a disposable association for variable specification reads.
- `--type-read-quarantine true` marks an IED/session as unsafe after a peer-close or transport fault.
- `--golden-scl <file>` or `samples/scl/<IED>.iid` supplies a trusted IEDScout/vendor IID reference for CDC/type learning.

The generated reports separate three different facts:

1. Core online discovery coverage.
2. Whether exact variable specification probing is safe for the target.
3. Which CDC/type mappings can be learned from a golden SCL reference.

This avoids forcing live type probes on IEDs that are already known to be sensitive.
