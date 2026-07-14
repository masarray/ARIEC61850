# Engine Maturity Matrix

This matrix is the current public capability and evidence boundary for ARIEC61850. It is intentionally conservative. A feature may be implemented without being broadly interoperable, production-ready, formally conformant, or approved for operational use.

## Evidence labels

| Label | Meaning |
|---|---|
| Implemented | Source path exists and is integrated |
| Unit tested | Deterministic automated tests cover the stated behavior |
| Loopback verified | Client/server or publisher/subscriber path is exercised locally |
| Laboratory exercised | A controlled test with external equipment has been recorded |
| Partial | Important behavior or negative cases remain incomplete |
| Not claimed | No public evidence supports the claim |

## Capability matrix

| Area | Current scope | Evidence | Important boundary |
|---|---|---|---|
| ASN.1 / BER / MMS codecs | Core encode/decode and confirmed-service structures | Implemented, unit tested | Malformed and negative corpus should continue expanding |
| TCP / TPKT / COTP / ACSE | Client association and simulator-side laboratory path | Implemented, unit tested, loopback verified | Broad endpoint variation and long-duration recovery remain under validation |
| MMS model discovery | Domains, named variables, FC-aware model, DataSets, RCB inventory, type inspection | Implemented, unit tested, laboratory exercised | Unknown and implementation-specific model variations remain possible |
| Data read | Typed and smart read workflows | Implemented, unit tested, laboratory exercised | Readability depends on server access and exposed type information |
| Generic write | Typed write planning and guarded execution | Implemented, unit tested | No claim of universal write support or operational approval |
| IEC 61850 control | Direct/SBO normal/enhanced sequencing, typed values, termination and application-error handling | Implemented, unit tested, one laboratory path exercised | Each IED family, model, firmware, CDC variant, and negative path requires validation |
| DataSet services | Directory read and dynamic define/delete foundations | Implemented, unit tested | Dynamic mutation remains guarded and must not occur during discovery |
| Reporting | RCB discovery, planning, guarded enable/GI, persistent monitoring, value projection | Implemented, unit tested, partial laboratory evidence | Full BRCB recovery, ownership variation, and long-duration reliability remain partial |
| GOOSE | Encode/decode, SCL profiles, publish/subscribe, sequence and supervision diagnostics | Implemented, unit tested, bounded laboratory use | No formal conformance, production timing, or broad traffic-corpus claim |
| Sampled Values | Encode/decode, payload generation, publishing, PCAP diagnostics | Implemented, unit tested, partial | Sustained subscriber, RMS/phasor analysis, and strong timing evidence remain future work |
| SCL | SCD/CID/ICD/IID parsing, communication profiles, type hints, expected-model analysis | Implemented, unit tested | Deep template resolution and station-wide dataflow validation continue to mature |
| PCAP | Read/write, replay, inspection, and expected-vs-observed binding | Implemented, unit tested | Public fixtures must remain synthetic or contributor-owned |
| Simulator | Deterministic model, loopback MMS server, discovery and read path | Implemented, unit tested, loopback verified | Not a production IED, conformance reference, or unrestricted interoperability claim |
| Evidence export | Markdown/JSON profiles, manifests, hashes, and workbench packs | Implemented, unit tested | Evidence quality depends on input provenance and test procedure |
| Security profiles | No IEC 62351 profile claim | Not claimed | Security and robustness testing remain separate from protocol feature coverage |
| Formal conformance | No formal certificate for a public release | Not claimed | Requires recognized testing for the exact release and declared scope |

## Current release gate

A developer-preview release should require:

```text
clean source
+ unambiguous GPL license metadata
+ successful restore/build/test
+ source-clean verification
+ consistent README/website/security/maturity wording
+ synthetic or documented-provenance fixtures
+ verified release-package contents
```

## Active-operation boundary

Control, report-control writes, GOOSE publishing, Sampled Values publishing, and packet replay can affect equipment state or network behavior. Protocol guardrails do not prove operational safety. Use active functions only with authority, approved procedures, isolation, and independent verification.

## Next evidence priorities

1. Multi-family control validation covering all four control models and negative completion paths.
2. Persistent reporting soak tests, reconnect, reservation, and BRCB recovery evidence.
3. Sustained Sampled Values subscriber and timing diagnostics.
4. Broader malformed-input and resource-limit tests.
5. Independent review of release claims, provenance records, and commercial-license ownership prerequisites.
