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
- MMS static and dynamic reporting have a guarded lab smoke path. The current
  receiver is suitable for short CLI sessions; a full long-running async receive
  pump, BRCB recovery, and multi-vendor soak evidence are still pending.
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

## Report subscription planning validation

Use these commands before enabling any report write workflow. They are intentionally read-only and are designed to validate the static and dynamic report paths without changing the IED state.

### Static report plan

```powershell
 dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-static-plan 192.16.1.157 --port 102 --timeout-ms 120000 --read-values --raw-limit 40
```

Expected result:

- one static RCB is selected, preferably a BRCB when available;
- the selected RCB has `DatSet` assigned and `RptEna=false`;
- the DataSet directory is decoded;
- DataSet members are shown as `LD/LN.DO.da [FC]`;
- optional sample reads return decoded values, quality, or timestamp structures.

### Dynamic report plan

```powershell
 dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-dynamic-plan 192.16.1.157 --port 102 --timeout-ms 120000 --points OCR7SR12PROT/A50PTOC1.Str,OCR7SR12PROT/A50PTOC1.Op --dataset-name AR_DYN_DS01
```

Expected result:

- requested points are resolved from the live IED directory;
- a free RCB slot is selected from `EmptyDynamicSlotNeedsDataSet` candidates;
- the proposed dynamic DataSet reference is shown;
- the execution steps explicitly list `CreateDataSet`, `Write RCB.DatSet`, reservation, `RptEna`, `GI`, and cleanup.

Live report-enable commands are guarded with `--yes`. Reporting is unsolicited,
so the stack queues `InformationReport` frames that arrive while a confirmed
response is pending. The remaining production hardening item is a full
association receive pump that can monitor reports while arbitrary confirmed
requests are in flight.

## Static report live smoke test (guarded)

After static planning and DataSet directory mapping are stable, use the guarded live report command only on an isolated test IED or an unused RCB:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-static-live 192.16.1.157 --port 102 --timeout-ms 120000 --duration-sec 15 --yes
```

Expected behavior:

1. The command discovers the live IED directory and report inventory.
2. It selects a static-ready RCB with a valid DataSet.
3. It writes `RptEna=true`, optionally writes `GI=true`, listens for
   InformationReport frames, then cleans up with `RptEna=false`. On the tested
   relay, explicit BRCB `ResvTms` pre-write is skipped because the relay accepts
   ownership through `RptEna=true` and rejects or side-effects explicit
   `ResvTms` writes.
4. Report values are mapped by DataSet member index and rendered with structured MMS values when available.

If no report is received, the write steps are still valuable evidence. Check `RptEna`, GI support, trigger options, RCB ownership, and whether the selected DataSet points are currently changing.

Recorded live static report result against `192.16.1.157`:

```text
RCB=OCR7SR12PROT/LLN0.BR.brcbA01
DataSet=OCR7SR12PROT/LLN0.DataSet
RptEna=true OK
GI=true OK
InformationReport frames=2
mapped DataSet values=2/2
cleanup RptEna=false OK
post-check RptEna=false
note=relay holds ResvTms after disable until its timer expires
```

## Dynamic report live smoke test (guarded)

Use the guarded dynamic report command only on an isolated test IED or a
confirmed unused dynamic RCB slot:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-report-dynamic-live 192.16.1.157 --port 102 --timeout-ms 120000 --rcb OCR7SR12MEAS/LLN0.BR.brcbA01 --points OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f,OCR7SR12MEAS/MMXU1.A.phsA.cVal.mag.f --dataset-name AR_DYN_DS01 --duration-sec 5 --gi true --yes
```

Recorded live dynamic report result:

```text
RCB=OCR7SR12MEAS/LLN0.BR.brcbA01
CreateDataSet=OCR7SR12MEAS/LLN0.AR_DYN_DS01 OK
Write RCB.DatSet OK
RptEna=true OK
GI=true OK
InformationReport frames=1
mapped DataSet values=2/2
cleanup RptEna=false OK
cleanup DatSet empty OK
DeleteDataSet=deleted 1/1 OK
post-check RptEna=false DatSet=empty
note=relay holds ResvTms after disable until its timer expires
```
