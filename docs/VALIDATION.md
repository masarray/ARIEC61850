# ARIEC61850 Validation Guide

Validation is part of the product. A protocol feature should not be called
usable until it has tests, sample commands, documented limitations, and a
validation note.

## Current automated checks

Run:

```powershell
dotnet build .\ARIEC61850.slnx -c Release
dotnet test .\ARIEC61850.slnx -c Release --no-build
```

Current evidence from the local validation run:

- Automated test suite is expected to pass before release packaging.
- BER reader/writer tests.
- MMS data value codec tests.
- MMS GetNameList and Confirmed-Read response decoder tests.
- MMS report inventory mapper tests.
- Live MMS directory, Smart FC resolver, and report readiness planner tests.
- COTP connection confirm parser tests.
- GOOSE frame round-trip tests.
- SV frame round-trip tests.
- SCL parser tests.
- SCL-backed publisher profile tests.
- SCL-backed publisher session tests.
- PCAP writer and reader tests.
- Process-bus stream monitor tests.
- GOOSE retransmission schedule tests.

## Current lab evidence

The first live SV publish path has been validated with:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-sv-live ".\samples\scl\01_SV_Stream_4I+4V_(9-2LE).scd" --adapter 5 --stream-index 1 --duration-sec 5 --yes --status-ms 1000
```

Recorded result:

```text
frames=20000
target rate=4000 Hz
elapsed=5.005s
effectiveRate=3995.682 fps
payloadBytes=64
```

The first live GOOSE publish path has been validated with:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- publish-goose-live .\samples\scl\minimal-station.scd --adapter 5 --stream-index 1 --duration-sec 5 --yes --toggle-every-sec 2
```

Recorded result:

```text
frames=26
duration=5 seconds
APPID=0x1001
destination=01:0C:CD:01:00:01
VLAN=100/prio 4
stNum=1..3
sqNum reset on state change
```

The first live MMS discovery path has been validated with:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-discover 192.16.1.157 --port 102 --timeout-ms 20000 --max-report-probes 16 --raw-limit 30
```

Recorded result:

```text
Association=MmsInitiated
ACSE profile=BalancedApTitle
logicalDevices=4
rawVariables=10122
datasets=1
reportControls=286
BRCB=8
URCB=278
```

The first live MMS directory and Smart FC read path has been validated with:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-directory 192.16.1.157 --port 102 --timeout-ms 60000 --show-points --raw-limit 120
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-resolve 192.16.1.157 OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-read-smart 192.16.1.157 OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f
```

Recorded result:

```text
Association=MmsInitiated
liveDirectory=LD:4 LN:123 FC-points:9464
reportAttrs=3456
controlAttrs=457
FC index=BR:120 CF:935 CO:457 DC:629 EX:14 MX:716 RP:3336 SP:6 ST:3251
Smart read=OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f [MX] => OK value=0
```

## Validation notes

- [SCL Publish MVP](validation/scl-publish-mvp.md)
- [Live SV Publish](validation/live-sv-publish.md)
- [Live GOOSE Publish](validation/live-goose-publish.md)
- [Live MMS Discovery](validation/live-mms-discovery.md)

## Limitations

- Live SV publisher timing is software-paced.
- Live publish is a lab smoke path, not protection-grade timing evidence.
- Current live publisher sends one selected SV stream per command.
- Current live GOOSE publisher sends one selected GOOSE stream per command.
- Typed engineering-value-to-SV payload binding is still evolving.
- MMS discovery is read-only; report enable/disable and InformationReport
  monitoring are not implemented yet.
- There is no conformance certification claim.

## Interoperability checklist

Before claiming wider interoperability:

- Validate with multiple vendor SCL files.
- Validate with Wireshark decode and at least one independent SV subscriber.
- Validate MMS discovery with multiple vendors and simulators.
- Add MMS report subscription PCAP/golden tests before enabling RCB writes by
  default.
- Add negative tests for malformed frames.
- Add PCAP corpus tests.
- Add hardware lab notes for adapter, driver, switch, and OS timing conditions.


## Live MMS DataSet directory validation

Run this after `mms-report-plan` shows at least one `ReadyStaticDataSet` candidate. The command is read-only and validates the DataSet member map required by report decoding:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-dataset-directory 192.16.1.157 OCR7SR12PROT/LLN0.DataSet --port 102 --timeout-ms 120000 --raw-limit 0
```

Expected result:

- Association reaches `MmsInitiated`.
- The command prints `DataSet directory: <dataset> members=<N>`.
- Each member is mapped to a live directory reference with FC when the IED exposes it through `GetNameList`, for example `OCR7SR12PROT/PTOC1.Str.general [ST]`.
- If the service is rejected by a device, keep the DataSet as `DirectoryUnavailable` and do not enable reporting automatically.

Optional sampled reads:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-dataset-directory 192.16.1.157 OCR7SR12PROT/LLN0.DataSet --read-values --raw-limit 16
```

Use `--read-values` only on test/engineering networks because it performs extra MMS read requests for selected DataSet members.
