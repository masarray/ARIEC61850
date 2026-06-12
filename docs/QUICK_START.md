# ARIEC61850 Quick Start

This quick start validates the current clean-room IEC 61850 stack from source.

## Requirements

- .NET 8 SDK.
- Windows for live Npcap publishing with the current transport.
- Npcap installed when using raw Ethernet commands.
- An isolated Ethernet adapter, TAP, or lab switch for active GOOSE/SV
  publishing.
- A lab IED or simulator for live MMS commands.

## Build and test

```powershell
dotnet restore .\ARIEC61850.slnx
dotnet build .\ARIEC61850.slnx -c Release
dotnet test .\ARIEC61850.slnx -c Release --no-build
```

## Inspect the 9-2LE sample SCL

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-scl ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd"
```

Expected high-level result:

- 3 IEDs.
- 3 Sampled Values streams.
- 16 DataSet entries per stream.
- SV destination MAC addresses `01:0C:CD:04:00:01` through
  `01:0C:CD:04:00:03`.

## Generate and inspect a PCAP

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\out\processbus-demo.pcap --sv-frames 32 --goose-frames 4
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-pcap .\out\processbus-demo.pcap
dotnet run --project .\apps\AR.Iec61850.Cli -- stream-pcap .\out\processbus-demo.pcap --delay-ms 50 --limit 20
```

## Publish SV to a lab adapter

List Npcap adapters:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- list-adapters
```

Dry-run first:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --frames 4000 --dry-run
```

Publish for one minute:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --duration-sec 60 --yes
```

Publish continuously until `Ctrl+C`:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --continuous --yes
```

Replace `--adapter 5` with the adapter index from your machine.

## Publish GOOSE to a lab adapter

Dry-run first:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-goose-live .\samples\scl\minimal-station.scd --adapter 5 --stream-index 1 --frames 8 --dry-run
```

Publish a bounded GOOSE stream:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-goose-live .\samples\scl\minimal-station.scd --adapter 5 --stream-index 1 --duration-sec 5 --toggle-every-sec 2 --yes
```

Publish continuously until `Ctrl+C`:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-goose-live .\samples\scl\minimal-station.scd --adapter 5 --stream-index 1 --continuous --toggle-every-sec 2 --yes
```

The GOOSE command uses the selected SCL `GSEControl`, APPID, destination MAC,
VLAN, `minTime`, and `maxTime`. Retransmissions increment `sqNum`; state
changes increment `stNum` and reset the retransmission schedule.

## Discover an MMS IED

Run a read-only MMS discovery against a lab IED:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-discover 192.16.1.157 --port 102 --timeout-ms 20000 --max-report-probes 16
```

Useful options:

- `--no-report-probe` lists domains, variables, DataSets, and RCB candidates
  without reading RCB attributes.
- `--max-report-probes N` bounds how many RCBs receive attribute reads.
- `--show-raw --raw-limit 0` prints all raw MMS names.

Build a live IED directory with automatic Functional Constraint parsing:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-directory 192.16.1.157 --port 102 --timeout-ms 20000 --show-points --raw-limit 40
```

Search, resolve, or smart-read a point without supplying FC manually:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-find 192.16.1.157 MMXU --fc MX --raw-limit 40
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-resolve 192.16.1.157 OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-read-smart 192.16.1.157 OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f
```

Plan report readiness before enabling any RCB workflow:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-plan 192.16.1.157 --port 102 --timeout-ms 60000 --max-report-probes 64 --only-safe
```

Inspect a live DataSet directory:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-dataset-directory 192.16.1.157 OCR7SR12PROT/LLN0.DataSet --port 102 --timeout-ms 60000 --raw-limit 80
```

Run a guarded static report smoke test only on an isolated lab IED or unused
RCB:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-static-live 192.16.1.157 --port 102 --timeout-ms 120000 --duration-sec 15 --yes
```

Run a guarded static report monitor on a selected RCB:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-monitor 192.16.1.157 --port 102 --timeout-ms 120000 --rcb OCR7SR12PROT/LLN0.BR.brcbA01 --duration-sec 60 --yes
```

Poll smart-read values while the report monitor is active:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-monitor 192.16.1.157 --port 102 --timeout-ms 120000 --rcb OCR7SR12PROT/LLN0.BR.brcbA01 --duration-sec 60 --poll-points OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f --poll-interval-ms 1000 --yes
```

Export report evidence artifacts:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-monitor 192.16.1.157 --port 102 --timeout-ms 120000 --rcb OCR7SR12PROT/LLN0.BR.brcbA01 --duration-sec 60 --poll-points OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f --poll-interval-ms 1000 --evidence .\out\report-session01 --yes
```

Run a guarded dynamic report smoke test only on a confirmed unused dynamic RCB
slot:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-dynamic-live 192.16.1.157 --port 102 --timeout-ms 120000 --points OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f,OCR7SR12MEAS/MMXU1.A.phsA.cVal.mag.f --dataset-name AR_DYN_DS01 --duration-sec 5 --gi true --delete-dataset true --yes
```

## Safety boundary

Active publishing sends raw multicast Ethernet frames. Use only an isolated lab
NIC, TAP, or test switch. Do not use a production substation network.

Live report commands write RCB/DataSet attributes. Use them only against lab
equipment and only after planning commands show a safe target.

### Evidence-grade report monitor

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-monitor 192.16.1.157 --port 102 --timeout-ms 120000 --duration-sec 60 --poll-points OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f --evidence out/report-evidence --yes
```

Review `verification.json`, `rcb-snapshots.json`, `dataset-snapshots.json`, and `summary.md` to confirm the IED state before enable, after enable, after GI/report reception, and after cleanup. `PASS_WITH_WARNING` is expected on relays that keep a BRCB `ResvTms` lease timer visible after `RptEna=false`; inspect the final `RptEna`, `DatSet`, and explicit reservation flag before treating it as a failure.


### Report forensic timeline evidence

Guarded report evidence now includes `report-timeline.json` and a Report Timeline section in `summary.md`. The timeline flattens each report into received time, RptID, DataSet, ConfRev, SqNum, EntryID, BufOvfl, included indexes, mapped count, reason summary, and decoded TimeOfEntry. Sequence diagnostics now distinguish reset-to-zero events from true regressions, while EntryID numeric gaps remain heuristic warnings because EntryID is treated as opaque by default. MMS `binary-time` is decoded to UTC/time-of-day when possible while retaining the original raw hex.

### Long-run report soak smoke test

For a longer stability check, run report monitoring with periodic polling, optional periodic GI, and soak snapshots:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-monitor 192.16.1.157 --port 102 --timeout-ms 900000 --duration-sec 600 --poll-points OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f --poll-interval-ms 1000 --gi-interval-sec 120 --soak-snapshot-sec 60 --evidence out/report-soak-10m --yes
```

The evidence folder includes `soak-snapshots.json` and a **Soak Snapshots** table in `summary.md`.

### Exact InformationReport decoder and report frame evidence

Report evidence now includes `report-frames.json`, `report-streams.json`, and `report-values.csv` in addition to `report-timeline.json`. The mapper first attempts an OptFlds-driven IEC 61850 report decode before falling back to the legacy inclusion-bitstring scan. Each report frame records `DecoderMode`, stream key (`RptID + DataSet + ConfRev`), parse warnings, optional-field bits/raw value, included indexes, reasons, and member-value mapping. The CSV is intended for quick FAT/SAT review in spreadsheet tools.

