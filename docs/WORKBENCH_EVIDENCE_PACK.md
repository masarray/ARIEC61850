# Workbench Evidence Pack

`workbench-evidence-pack` generates a folder-based review package for the current engine alpha path. It is designed for repeatable engineering review, CI smoke checks, and WPF export without turning the desktop app into a protocol-logic container.

## Scope

The pack is read-only and combines:

```text
SCL engineering profile
→ optional PCAP observation
→ expected-vs-observed process-bus binding
→ GOOSE diagnostics
→ Sampled Values diagnostics
→ MMS read-only loopback alpha
→ optional public-alpha readiness gate
→ manifest + artifact hashes
```

It is not a conformance certificate. Missing GOOSE/SV observations are expected when no PCAP or live capture is provided.

## CLI

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- workbench-evidence-pack `
  --scl .\samples\scl\minimal-station.scd `
  --output .\.artifacts\workbench-pack
```

With PCAP evidence:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\processbus-demo.pcap

dotnet run --project .\apps\AR.Iec61850.Cli -- workbench-evidence-pack `
  --scl .\samples\scl\minimal-station.scd `
  --pcap .\.artifacts\out\processbus-demo.pcap `
  --output .\.artifacts\workbench-pack
```

Useful options:

| Option | Meaning |
| --- | --- |
| `--scl <file>` | SCL source file. Defaults to `samples/scl/minimal-station.scd`. |
| `--pcap <file>` | Optional PCAP observation source. |
| `--output <folder>` | Evidence pack output folder. |
| `--no-public-alpha` | Skip the aggregate public-alpha readiness profile. |
| `--nominal-hz 50` | Nominal frequency used by process-bus monitor. |
| `--port 0` | Use ephemeral loopback port for MMS read-only loopback. |
| `--timeout-ms 5000` | Loopback probe timeout. |
| `--steps N` | Simulation steps for virtual model before server loopback. |

## Folder layout

```text
workbench-pack/
├─ README.md
├─ manifest.json
└─ profiles/
   ├─ scl-engineering-profile.md
   ├─ scl-engineering-profile.json
   ├─ process-bus-binding-profile.md
   ├─ process-bus-binding-profile.json
   ├─ goose-diagnostics-profile.md
   ├─ goose-diagnostics-profile.json
   ├─ sv-diagnostics-profile.md
   ├─ sv-diagnostics-profile.json
   ├─ mms-readonly-loopback-profile.md
   ├─ mms-readonly-loopback-profile.json
   ├─ public-alpha-readiness-profile.md
   └─ public-alpha-readiness-profile.json
```

`manifest.json` contains an artifact index with byte size and SHA-256 for each generated file.

## WPF integration

The Engineering Workbench `Export pack` action uses the same engine builder as the CLI command. The WPF app remains a thin harness: protocol analysis, diagnostics, MMS loopback, and pack generation all live in engine libraries under `src`.
