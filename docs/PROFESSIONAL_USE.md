# Professional Use

ARIEC61850 is designed as an engineering foundation for IEC 61850 test tools,
lab automation, and future workstation applications.

## Appropriate use

Use this repository for:

- Building IEC 61850 protocol tooling.
- Testing clean-room frame builders and parsers.
- Inspecting SCL engineering files.
- Generating PCAP evidence for review.
- Publishing Sampled Values in an isolated lab.
- Publishing GOOSE in an isolated lab.
- Discovering and reading MMS IED models in a lab.
- Planning and running guarded MMS report smoke tests on unused lab RCBs.
- Building future MMS, GOOSE, SV, file-transfer, simulator, and workbench
  applications.

## Safety rules

- Treat active publishers as lab tools.
- Use an isolated test NIC, TAP, or lab switch.
- Do not connect active publish commands to production substation networks.
- Do not run report live commands against production IEDs or RCBs used by
  another client.
- Do not issue future MMS file/control behavior without explicit operator
  confirmation in the application layer.
- Pair tool output with approved FAT, SAT, or commissioning procedures.

## Evidence discipline

For professional project records:

- Save SCL source files used for a test.
- Record adapter and driver configuration.
- Capture PCAP evidence when possible.
- Record tool version or commit hash.
- Attach validation notes and known limitations.
- Record the selected RCB, DataSet, report options, and cleanup result for any
  live report smoke test.

## Current maturity

The current repository is a stack and tester foundation, not a certified IEC
61850 conformance tool. It should be used as supporting evidence and engineering
automation until formal interoperability and conformance validation are added.
