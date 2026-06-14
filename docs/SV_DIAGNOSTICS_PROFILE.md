# Sampled Values Diagnostics Profile

The Sampled Values diagnostics profile is a read-only engine milestone. It compares SCL expected SampledValueControl streams against observed PCAP/live process-bus summaries and emits deterministic Markdown/JSON evidence.

It is designed for offline validation first, then live capture later.

## What it checks

- expected SV stream missing;
- unexpected observed SV stream;
- APPID mismatch;
- destination MAC mismatch;
- VLAN mismatch;
- `confRev` mismatch;
- `nofASDU` mismatch;
- sample-rate/sample-mode mismatch;
- payload length and DataSet decode issues;
- `smpCnt` gap, missed sample, duplicate, out-of-order, and wrap;
- `smpSynch` not indicating synchronized operation.

## Offline validation

Generate a deterministic diagnostic PCAP:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\sv-diagnostic-demo.pcap --goose-frames 0 --sv-scenario diagnostic
```

Run the diagnostics profile:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- sv-diagnostics-profile .\samples\scl\minimal-station.scd .\.artifacts\out\sv-diagnostic-demo.pcap --output .\.artifacts\out\sv-diagnostics.md --json .\.artifacts\out\sv-diagnostics.json
```

Expected result: the command should report SV findings for sample counter, synchronization, payload, and model-alignment conditions, then write Markdown/JSON evidence under `.artifacts\out`.

## Engine contract

```text
SCL expected SampledValueControl stream
→ observed ProcessBusStreamSummary
→ SV sequence/payload/synchronization checks
→ typed findings
→ Markdown/JSON evidence
```

This is the foundation for the future SV analyzer, process-bus forensic workspace, and station-level expected-vs-observed validation.
