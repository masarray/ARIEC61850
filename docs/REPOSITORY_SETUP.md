# Repository Setup

This file records the recommended GitHub settings for ARIEC61850.

## Repository description

```text
Clean-room IEC 61850 native .NET stack for MMS, GOOSE, Sampled Values, SCL import, PCAP validation, and live SV publishing.
```

## Website

```text
https://masarray.github.io/ARIEC61850/
```

## Topics

```text
iec61850
iec-61850
mms
goose
sampled-values
sv-publisher
scl
substation-automation
scada
process-bus
protocol-analyzer
commissioning
fat-testing
sat-testing
dotnet
csharp
npcap
pcap
clean-room
substation
```

## GitHub Pages

Pages is deployed from `.github/workflows/deploy-pages.yml` using the `docs/`
folder as the static site artifact.

Recommended Pages source:

```text
GitHub Actions
```

## Automation

- `.NET CI` builds and tests the solution.
- `Deploy GitHub Pages` publishes the landing page and documentation site.

## Source-only hygiene

The `.gitignore` is configured to keep generated artifacts out of the repository:

- `bin/`
- `obj/`
- `out/`
- PCAP and capture files
- coverage files
- local adapter/session files
- compiled binaries and symbol files
