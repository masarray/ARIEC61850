# SV Engine Audit

Date: 2026-06-13

## Scope

This audit covers the current ARIEC61850 Sampled Values publisher engine:

- Ethernet/VLAN SV frame builder/parser.
- SCL-backed `SampledValueControl` stream selection.
- DataSet-order payload generation.
- `smpCnt` progression.
- CLI and WPF publisher integration.

Subscriber decode, multi-ASDU publishing, engineering-unit scaling, PTP-grade
timing, R-SMV, and formal conformance are not claimed by this patch.

## Standards And Reference Basis

- IEC 61850-9-2 maps sampled values to ISO/IEC 8802-3 Ethernet and defines
  SVCB evolution, reserved link-layer fields, and sampled-value buffer
  encoding changes in the 2020 amendment.
- IEC 61850-6 is the engineering source for SCL files, including DataSet order,
  communication addressing, and `SampledValueControl`.
- IEC/IEEE 61850-9-3 is the time-synchronization profile relevant to SV timing
  quality. ARIEC61850 does not yet claim 9-3/PTP-grade timing.
- libiec61850's public API confirms the practical publisher contract: create a
  publisher from communication parameters, add ASDU with `svID`, `datset`, and
  `confRev`, set ASDU attributes such as `smpCnt`, `smpRate`, `smpMod`,
  `smpSynch`, optional `refrTm`, then publish.
- libiec61850's public subscriber API confirms a key design point: SV
  measurement data is raw binary and cannot be interpreted correctly without
  a priori DataSet/layout knowledge. ARIEC61850 now treats SCL layout as the
  first-class decode contract instead of exposing only manual offsets.
- DigSubAnalyzer is a useful Apache-2.0 reference for product direction:
  passive process-bus visibility, stream summaries, PTP/timing wording, and
  commissioning diagnostics. ARIEC61850 should reuse compatible ideas as
  product requirements, while keeping the protocol stack typed and reusable.

Sources used:

- https://webstore.iec.ch/en/publication/66549
- https://webstore.iec.ch/en/publication/103863
- https://webstore.iec.ch/en/publication/24998
- https://support.mz-automation.de/doc/libiec61850/c/latest/group__sv__publisher__api__group.html
- https://support.mz-automation.de/doc/libiec61850/c/latest/group__sv__publisher__asdu__group.html
- https://support.mz-automation.de/doc/libiec61850/c/latest/group__sv__subscriber__api__group.html
- https://github.com/masarray/DigSubAnalyzer

## Findings Before N5.17

- The frame codec was already useful: Ethernet, optional VLAN, APPID, reserved
  fields, SAV PDU, ASDU, `svID`, DataSet reference, `smpCnt`, `confRev`,
  optional `refrTm`, `smpSynch`, `smpRate`, `smpMod`, and raw payload all
  round-tripped.
- The publisher profile already came from SCL, but payload creation still lived
  in CLI/WPF application code.
- Payload generation assumed most non-quality values were 4-byte signed
  integers. That matched the included 4I+4V sample but was not a reusable SV
  engine.
- `nofASDU` was parsed but not enforced. A multi-ASDU SCL could accidentally be
  transmitted as a one-ASDU stream.
- `smpCnt` only wrapped at `ushort.MaxValue`. Common process-bus profiles need a
  rate/profile-aware wrap strategy, for example 4000 samples per second.

## N5.17 Changes

- Added `SampledValuesPayloadLayout` to map SCL DataSet entries to raw SV
  payload offsets, widths, element kinds, and diagnostics.
- Added `SampledValuesPayloadBuilder` to build raw SV data blocks from typed
  MMS-style values and deterministic demo waveforms.
- Added profile-level payload helpers and `ResolveSampleCounterWrap`.
- Added session-level optional `sampleCounterWrap`.
- Updated CLI `publish-sv-live` to use the core payload layout and expose
  `--smpcnt-wrap auto|none|N`.
- Updated WPF SV Publisher to reuse the same layout engine instead of a separate
  4-byte-per-entry generator.
- Added fail-fast validation for `nofASDU > 1` until multi-ASDU support exists.

## N5.18 Changes

- Added `SampledValuesPayloadDecoder`, the receive-side counterpart to the
  layout-driven payload builder.
- Added typed decode for Boolean, signed/unsigned integer widths including
  UINT24, FLOAT32/FLOAT64, enum, bit-string/quality, timestamp, entry-time,
  octet string, and visible string payload elements.
- Added optional SCL binding to `ProcessBusStreamMonitor` so SV frames can be
  interpreted by APPID, destination MAC, `svID`, DataSet reference, and
  `confRev` evidence rather than raw offsets only.
- Added per-stream `smpCnt` diagnostics: first/in-sequence/wrapped, jump/loss,
  missed sample count, duplicate count, and out-of-order/late frame count.
- Extended `inspect-pcap` and `stream-pcap` with `--scl` and `--nominal-hz` so
  PCAP replay can become a passive SV subscriber/analyzer surface.

## Comparison With libiec61850

| Area | libiec61850 public capability | ARIEC61850 status |
| --- | --- | --- |
| SV publisher frame/API | Mature C API with publisher, ASDU add, setup, publish, VLAN/appID parameters. | Lab MVP publisher with SCL profile, Ethernet/VLAN builder, Npcap live send, dry-run and PCAP generation. |
| ASDU attributes | Public setters for `smpCnt`, wrap, `smpRate`, `smpMod`, `smpSynch`, `refrTm`. | `smpCnt`, `smpRate`, `smpMod`, `smpSynch`, `refrTm`, and profile-aware wrap are modeled. |
| Payload layout | Subscriber API exposes raw byte access by manual index/offset and documented type widths. | SCL DataSet order creates a typed payload layout automatically; publisher and subscriber share it. |
| Subscriber receive | Receiver/subscriber callback API for live traffic. | Passive PCAP monitor/subscriber decode is implemented; live raw-NIC subscriber loop is still next. |
| Sequence diagnostics | Application can inspect values/counters, but public API is low-level. | Stack now emits commissioning-oriented gap, missed, duplicate, out-of-order, and wrap counters. |
| SCL integration | Full stack also supports SCL/control-block handling in broader library. | SCL parser binds `SampledValueControl`, Communication/SMV, DataSet entries, and payload decode for current profiles. |
| Multi-ASDU | Supported by libiec61850 API model. | Explicitly rejected for publisher until implemented; decode path still focuses on first ASDU in monitor. |
| R-SMV / 90-5 | Public README marks R-session/R-GOOSE/R-SMV as beta. | Not implemented. |
| Timing quality | Library can publish, but timing quality depends on host/platform. | Windows/Npcap publisher is lab/screening-level; PTP/hardware timestamp evidence is not claimed. |

## Comparison With DigSubAnalyzer

DigSubAnalyzer is closer to a product analyzer than a reusable protocol stack.
Its useful direction for ARIEC61850 is passive process-bus visibility:
stream summaries, diagnostics, PTP/timing confidence wording, and FAT/SAT
operator workflows. ARIEC61850 should not collapse into a UI-only analyzer; the
stack advantage is reusable typed models, SCL binding, builder/decoder symmetry,
and CLI/WPF/test surfaces fed by the same core.

## Current Acceptance Evidence

```powershell
dotnet build .\ARIEC61850.slnx -c Release
dotnet build .\apps\AR.Iec61850.SvPublisher\AR.Iec61850.SvPublisher.csproj -c Release
dotnet test .\ARIEC61850.slnx -c Release --no-build
```

Result:

```text
Build succeeded, 0 warnings, 0 errors.
Tests passed: 134/134.
WPF SV Publisher build succeeded.
```

After N5.18:

```text
Build succeeded, 0 warnings, 0 errors.
Tests passed: 138/138.
WPF SV Publisher build succeeded.
```

CLI dry-run:

```powershell
dotnet .\apps\AR.Iec61850.Cli\bin\Release\net8.0\AR.Iec61850.Cli.dll publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 1 --source-mac 02:00:00:00:20:01 --stream-index 1 --frames 3 --dry-run
```

Observed:

```text
datasetEntries=16
payloadBytes=64
smpCntWrap=4000
sent=1/3 smpCnt=0 payloadBytes=64
sent=2/3 smpCnt=1 payloadBytes=64
sent=3/3 smpCnt=2 payloadBytes=64
```

SCL-bound PCAP subscriber/analyzer smoke:

```powershell
dotnet .\apps\AR.Iec61850.Cli\bin\Release\net8.0\AR.Iec61850.Cli.dll generate-pcap .\samples\scl\minimal-station.scd .\out\n5-18-sv-demo.pcap
dotnet .\apps\AR.Iec61850.Cli\bin\Release\net8.0\AR.Iec61850.Cli.dll inspect-pcap .\out\n5-18-sv-demo.pcap --scl .\samples\scl\minimal-station.scd
dotnet .\apps\AR.Iec61850.Cli\bin\Release\net8.0\AR.Iec61850.Cli.dll stream-pcap .\out\n5-18-sv-demo.pcap --scl .\samples\scl\minimal-station.scd --delay-ms 0 --limit 3
```

Observed:

```text
SV streams: 1 frames=16
APPID=0x4001 ... packets=16 smpCnt=0..15 values=2 gaps=0 missed=0 dup=0 late=0 wraps=0

smpCnt=0 seq=First payloadBytes=8 bound=SCL values=2
smpCnt=1 seq=InSequence payloadBytes=8 bound=SCL values=2
smpCnt=2 seq=InSequence payloadBytes=8 bound=SCL values=2
```

## Remaining Gaps

- Multi-ASDU publisher support.
- Live raw-NIC SV subscriber receive loop.
- Multi-ASDU subscriber event routing and typed decode for all ASDUs.
- SCL engineering-unit scaling from DO/DA metadata, not only demo dLSB settings.
- Full quality bit semantics and time-quality handling in generated channel data.
- Hardware-timestamp or PTP-aware pacing validation.
- R-SMV / IEC 61850-90-5 transport support.
- Formal conformance evidence against independent SV subscriber tools.

## Next Safest Patch

Build a bounded live SV subscriber before adding more publisher features:

1. Add a live Npcap receive transport path for EtherType `0x88BA`.
2. Reuse `ProcessBusStreamMonitor` for live frames and PCAP frames.
3. Add filter-by APPID, destination MAC, `svID`, and source MAC.
4. Add bounded CLI `monitor-sv-live` with explicit SCL
   binding and anonymous mode when SCL is missing.
5. Add capture/evidence output with stream summary JSON and sample diagnostics.
