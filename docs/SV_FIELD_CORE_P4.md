# Sampled Values field core P4

This field layer turns generic protocol evidence into a practical, explicitly qualified workflow without introducing manufacturer-specific decoding.

## Health axes

- `CAPTURE`: packet availability to the application.
- `PROTOCOL`: Ethernet and Sampled Values APDU decoding.
- `STREAM`: sample continuity and payload consistency.
- `CONFIGURATION`: expected CID/SCD configuration versus observed traffic.
- `MEASUREMENT`: channel semantics, scaling, CT/VT context, signal activity, and validation confidence.

Operational health uses CAPTURE, PROTOCOL, and STREAM. Configuration or measurement uncertainty is reported independently and does not make a clean stream BAD.

## File replay

`ProcessBusCaptureFileReader` reads classic PCAP and PCAPNG Enhanced Packet Blocks with Ethernet link type. Offline packets feed the same `SampledValuesFrameParser` used by live capture.

## Signal evidence

`SvSignalStateAnalyzer` uses robust statistics and a coherent-fundamental estimate. It never changes raw samples. QUIET requires an explicit rated or absolute threshold; otherwise non-coherent small activity is reported as `NoiseDominated` rather than falsely forced to zero.

## SCL binding

`SvSclBindingScorer` treats APPID and destination MAC as strong identity evidence, while optional `datSet` may be absent on wire. `confRev` disagreement is retained as configuration evidence. A VLAN tag absent from a host capture can remain indeterminate because NIC or driver offload may strip it.

## Field acceptance boundary

Deterministic tests prove software behavior only. Real PCAPNG/CID replay, known injection, and authorized live MU tests remain required before calibrated measurement or universal-interoperability claims.
