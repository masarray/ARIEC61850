# Engine Maturity Matrix

This matrix is the public engineering checklist for growing ARIEC61850 from protocol foundation into a smart IEC 61850 stack. It is intentionally engine-first. UI applications should consume these capabilities; they should not define protocol behavior.

| Engine area | Current level | Next testable increment | Public-ready target |
|---|---|---|---|
| ASN.1/BER/MMS codec | Implemented with unit tests | Add malformed/negative golden PDUs | Stable codec golden corpus |
| OSI association | TCP/TPKT/COTP/ACSE client path | Add association profile diagnostics | Reconnect and session recovery evidence |
| MMS model discovery | Online GetNameList-based discovery | High-level ACSI model-browser facade | Live model snapshot export with typed findings |
| Data read/write | Basic read/write and smart read | Service-result facade and more typed values | Safe data reader with explicit write guard |
| DataSet service | Directory read + dynamic define/delete basics | DataSet readiness diagnostics | Static/dynamic DataSet workflows with evidence |
| Reporting | RCB discovery, planner, guarded live session, static readiness profile | Typed RCB state machine, BRCB recovery, and profile import | URCB/BRCB runtime with GI/recovery/evidence |
| GOOSE | Encode/decode/publish/subscribe basics | Expected-vs-observed diagnostics | SCL-bound forensic engine |
| Sampled Values | Encode/decode/publish/injector basics | Subscriber/analyzer engine | RMS/phasor/timing/continuity diagnostics |
| SCL | Parser/exporter/diff basics | Deep type-template and communication resolver | Station dataflow graph and mapping validator |
| Simulation | Offline profile + read-only server model + loopback TCP listener skeleton | TPKT/COTP/ACSE/MMS read-only listener alpha | Virtual IED with reports, GOOSE, SV scenarios |
| File/log/setting | Not yet mature | Read-only client browser first | Typed ACSI services with guarded writes |
| Security diagnostics | Not a base feature yet | Rule-based semantic checks | Explainable cyber/semantic findings without black-box claims |

## Next-level test contract

Every new engine feature should include one deterministic test path:

```text
input fixture → engine service → typed result → diagnostic/evidence assertion
```

Live hardware tests are useful, but they must not be the only validation method. Each live workflow should have at least one synthetic or golden-fixture equivalent.

## Naming rule

Public source, docs, CLI output, tests, and release packages must use neutral ARIEC61850 terminology. Do not use benchmark-product names as feature names, profiles, commands, or comments.

## N5.24 report-readiness test contract

The report readiness profile is the first engine contract that bridges discovery into safe runtime preparation without enabling an RCB. It must remain read-only and deterministic.

```text
live/synthetic discovery + DataSet directories
→ static report readiness profile
→ acceptance gates
→ RCB candidate matrix
→ selected guarded session profile
→ Markdown/JSON evidence
```

The profile is considered ready only when model discovery, DataSet directory member mapping, RCB selection, and member-map gates are satisfied. `RptEna` and `GI` are still live-write actions and must remain behind explicit caller confirmation.


## N5.25 — SCL Deep Engineering Profile

This milestone adds an offline SCL engineering profile engine. It extracts access points, server/logical-device/logical-node structure, expected report sessions, expected GOOSE/SV streams, subscriber ExtRef mapping, service declarations, and static findings. The profile is available through `scl-engineering-profile` and is designed as the expected-model input for future report, GOOSE, SV, simulator, and evidence engines.

## N5.26 — Expected-vs-Observed Process-Bus Binding

This milestone bridges the SCL engineering profile with observed process-bus summaries from PCAP/live capture. It provides typed findings for missing expected GOOSE/SV streams, unexpected observed streams, APPID/MAC/VLAN/confRev mismatch, optional DataSet value-count mismatch, and sequence/timing anomalies.

```text
SCL expected GOOSE/SV stream
→ observed ProcessBusStreamSummary
→ binding profile
→ finding/evidence
```

This is the foundation for later GOOSE forensic diagnostics, SV analyzer diagnostics, station dataflow validation, and simulator verification.


## N5.27 — GOOSE Diagnostics Profile

This milestone promotes GOOSE from basic decode/binding into an explainable diagnostic profile. It provides typed findings for missing expected publishers, unexpected observed publishers, APPID/MAC/VLAN/confRev mismatch, DataSet value-count mismatch, `stNum`/`sqNum` gaps and regressions, duplicate frames, supervision timeout, test flag, needs-commissioning flag, and suspicious value changes without a state-number increment.

```text
SCL expected GOOSE stream
→ observed PCAP/live GOOSE summary
→ sequence/supervision semantic checks
→ Markdown/JSON evidence
```

Maturity impact: process-bus GOOSE moves from visibility to actionable engineering diagnostics.


## N5.28 — Sampled Values Diagnostics Profile

This milestone promotes SV from basic publish/decode/binding into an explainable diagnostic profile. It provides typed findings for missing expected streams, unexpected observed streams, APPID/MAC/VLAN/confRev mismatch, `nofASDU` mismatch, sample-rate/sample-mode mismatch, payload decode issues, `smpCnt` gaps, missed samples, duplicates, out-of-order samples, wraps, and `smpSynch` issues.

```text
SCL expected SV stream
→ observed PCAP/live SV summary
→ sample-counter/payload/synchronization semantic checks
→ Markdown/JSON evidence
```

Maturity impact: process-bus SV moves from visibility to actionable engineering diagnostics.

## Server-side milestone

- MMS read-only server alpha: implemented as offline virtual IED profile + high-level service handler.
- MMS listener skeleton profile: implemented as loopback TCP listener lifecycle + JSON-line probe harness + write guard evidence.
- Next maturity gate: attach TPKT/COTP/ACSE/MMS decoding/encoding to the read-only listener while preserving the same service contract.


## N5.30 — MMS Listener Skeleton Profile

This milestone adds the first live transport boundary for the simulator-backed server model. The listener binds to a loopback TCP endpoint, accepts a client session, dispatches deterministic read-only service requests, verifies write rejection, and exports Markdown/JSON evidence.

```text
virtual IED profile
→ read-only service handler
→ TCP listener skeleton
→ loopback probe
→ listener evidence
```

Scope boundary: the harness intentionally uses a JSON-line probe protocol. The next milestone should replace the probe decoder with TPKT/COTP/ACSE/MMS request handling while keeping the same read-only service semantics.

### N5.31 MMS handshake codec profile

N5.31 adds an offline handshake codec evidence path for the server-side roadmap. It validates TPKT framing, COTP CR/CC/Data TPDU handling, and ISO Session / ACSE / MMS association payload inspection before the listener skeleton is upgraded to real MMS PDU handling.

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-handshake-codec-profile --output .\.artifacts\out\mms-handshake-codec.md --json .\.artifacts\out\mms-handshake-codec.json
```



## N5.32 - MMS Handshake Listener Profile

Status: source milestone. The engine now includes a loopback OSI listener probe that validates TCP listener lifecycle, TPKT frame exchange, COTP CR/CC handshake, COTP Data TPDU reception, and ACSE/MMS association payload inspection. This remains a transport skeleton, not a full MMS server. The next protocol milestone is ACSE AARE + MMS initiate response.

## N5.33 MMS Association Response Profile

N5.33 adds a loopback server-side association response probe. The engine now accepts a TPKT/COTP transport association, receives an ACSE/MMS associate request payload, sends a deterministic ACSE AARE + MMS InitiateResponse profile, and exports Markdown/JSON evidence. This remains a safe protocol gate before live confirmed MMS request dispatch.

Test command:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-association-response-profile --port 0 --output .\.artifacts\out\mms-association-response.md --json .\.artifacts\out\mms-association-response.json
```


## N5.34 confirmed-request skeleton gate

The server lifecycle now includes a deterministic confirmed-request dispatch path after ACSE/MMS association. The profile validates directory/read/DataSet request dispatch and write rejection over TPKT/COTP loopback frames. Full MMS ConfirmedRequest BER decoding remains a future maturity gate.
