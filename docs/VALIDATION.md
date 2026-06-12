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

- 23 tests passed.
- BER reader/writer tests.
- MMS data value codec tests.
- GOOSE frame round-trip tests.
- SV frame round-trip tests.
- SCL parser tests.
- SCL-backed publisher profile tests.
- SCL-backed publisher session tests.
- PCAP writer and reader tests.
- Process-bus stream monitor tests.

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

## Validation notes

- [SCL Publish MVP](validation/scl-publish-mvp.md)
- [Live SV Publish](validation/live-sv-publish.md)

## Limitations

- Live SV publisher timing is software-paced.
- Live publish is a lab smoke path, not protection-grade timing evidence.
- Current live publisher sends one selected SV stream per command.
- Typed engineering-value-to-SV payload binding is still evolving.
- MMS transport layers are planned, not complete.
- There is no conformance certification claim.

## Interoperability checklist

Before claiming wider interoperability:

- Validate with multiple vendor SCL files.
- Validate with Wireshark decode and at least one independent SV subscriber.
- Add negative tests for malformed frames.
- Add PCAP corpus tests.
- Add hardware lab notes for adapter, driver, switch, and OS timing conditions.
