# Process-Bus Binding Profile

The process-bus binding profile compares the static SCL engineering model against observed process-bus traffic from a PCAP file. It is an engine-first diagnostic layer for future report, GOOSE, SV, simulator, and evidence applications.

```text
SCL expected model
→ observed PCAP/live summaries
→ expected-vs-observed binding
→ typed findings
→ Markdown/JSON evidence
```

The first implementation covers GOOSE and Sampled Values streams. It checks expected stream presence, unexpected publishers, APPID, destination MAC, VLAN, configuration revision, DataSet value count when available, and sequence/timing anomalies from the process-bus monitor.

## Offline validation

Generate a deterministic PCAP from the sample SCL:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\processbus-demo.pcap
```

Build the expected-vs-observed binding evidence:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- process-bus-binding-profile .\samples\scl\minimal-station.scd .\.artifacts\out\processbus-demo.pcap --output .\.artifacts\out\process-bus-binding.md --json .\.artifacts\out\process-bus-binding.json
```

Exit code policy:

| Exit code | Meaning |
|---:|---|
| 0 | No high-severity process-bus binding finding |
| 3 | At least one high-severity binding finding, such as missing expected stream or critical mismatch |

## Finding examples

| Code | Meaning |
|---|---|
| `PB_GOOSE_EXPECTED_MISSING` | SCL defines a GOOSE stream but no matching observed stream was found |
| `PB_SV_EXPECTED_MISSING` | SCL defines an SV stream but no matching observed stream was found |
| `PB_UNEXPECTED_OBSERVED_STREAM` | A GOOSE/SV stream was observed but is not expected by the SCL profile |
| `PB_GOOSE_APPID_MISMATCH` | Observed GOOSE APPID differs from SCL |
| `PB_GOOSE_DESTINATION_MAC_MISMATCH` | Observed GOOSE destination MAC differs from SCL |
| `PB_GOOSE_CONFREV_MISMATCH` | Observed GOOSE confRev differs from SCL |
| `PB_SV_SAMPLE_GAP` | Observed SV sample counter gaps/missed samples |
| `PB_SV_OUT_OF_ORDER` | Observed SV samples are out of order |

The profile is read-only. It does not publish traffic and does not write to an IED.
