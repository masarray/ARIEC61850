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
| Simulation | Offline deterministic profile engine | Read-only MMS server adapter | Virtual IED with reports, GOOSE, SV scenarios |
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
