# GOOSE Diagnostics Profile

The GOOSE diagnostics profile is a read-only process-bus evidence engine. It compares expected GOOSE streams from an SCL engineering profile with observed GOOSE traffic from a PCAP or live capture summary, then produces Markdown/JSON findings.

This is an engine milestone, not a product UI. Desktop apps and CLI commands remain validation harnesses until product applications are split into dedicated repositories.

## What it checks

The profile currently evaluates:

- expected publisher missing;
- unexpected observed publisher;
- APPID mismatch;
- destination MAC mismatch;
- VLAN mismatch;
- `confRev` mismatch;
- DataSet value-count mismatch when decoded values are available;
- `stNum` / `sqNum` gaps;
- `stNum` / `sqNum` regression;
- duplicate frames;
- supervision timeout against the previous `TimeAllowedToLive`;
- test flag and needs-commissioning flag;
- suspicious value changes without state-number increment;
- state-number change without decoded value change.

## Offline test path

Generate a deterministic GOOSE diagnostic PCAP from the sample SCL:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\goose-diagnostic-demo.pcap --sv-frames 0 --goose-scenario diagnostic
```

Run the diagnostic profile:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- goose-diagnostics-profile .\samples\scl\minimal-station.scd .\.artifacts\out\goose-diagnostic-demo.pcap --output .\.artifacts\out\goose-diagnostics.md --json .\.artifacts\out\goose-diagnostics.json
```

The diagnostic scenario intentionally injects sequence gaps, supervision timeout, test/needs-commissioning flags, value-change-without-state evidence, and an additional unmatched stream variant so the finding engine can be exercised without physical IED hardware.

## Intended engine role

```text
SCL expected GOOSE stream
→ PCAP/live observed GOOSE summary
→ sequence and supervision analysis
→ semantic findings
→ Markdown/JSON evidence
```

This profile is the foundation for future station-level validation, live capture reporting, and product-app diagnostics.
