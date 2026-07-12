# Changelog

All notable public changes to ARIEC61850 are recorded here. The project is still evolving toward stable semantic releases; entries are grouped by engineering milestone until a formal versioning policy is adopted.

## Unreleased — Smart Control and public release refresh

### Added

- Native IEC 61850 client-side control-object service in `AR.Iec61850.Control`.
- Automatic detection and execution of Direct Operate and Select-Before-Operate, normal and enhanced security models.
- Typed live `ctlVal` binding for DPC, SPC, INC/ISC, BSC, APC, and validated raw vendor variants.
- Immutable sequence handling for `ctlNum`, `T`, origin, Test, interlock check, and synchrocheck.
- CommandTermination, LastApplError, ControlError, and AddCause decoding.
- SBO ownership, timeout, best-effort Cancel, association-loss cleanup, and concurrency guards.
- Guarded WPF Smart Control Tester with simple OPEN/CLOSE intent, live status feedback, advanced origin/check settings, safety arming, and command evidence.
- Live laboratory IED validation record for the end-to-end control request path.
- Dedicated Smart Control website page and refreshed search/social metadata.

### Changed

- Repositioned the public README around the core IEC 61850 engineering use cases rather than internal milestone notes.
- Moved detailed capability history to documentation and changelog files.
- Improved GitHub Pages information architecture, accessibility, mobile layout, and technical SEO.
- Updated documentation navigation and public validation boundaries.

### Fixed

- Missing `System.IO` namespace in the WPF Control Tester.
- Expired SBO selection state returning `Rejected` instead of deterministic `TimedOut`.
- xUnit analyzer findings for collection assertions.
- Duplicate milestone sections in the previous README.

## Previous engineering milestones

Earlier milestones include MMS association and discovery, reporting readiness and monitoring, GOOSE/SV codecs and diagnostics, SCL engineering profiles, PCAP workflows, deterministic simulation, Npcap transport, SV injection workspaces, and engineering evidence export.

See [docs/FULL_STACK_ROADMAP.md](docs/FULL_STACK_ROADMAP.md) and [docs/ENGINE_MATURITY_MATRIX.md](docs/ENGINE_MATURITY_MATRIX.md) for the detailed capability history and remaining work.
