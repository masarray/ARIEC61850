# Changelog

All notable public changes to ARIEC61850 are recorded here. The project is still evolving toward stable semantic releases; entries are grouped by engineering milestone until a formal versioning policy is adopted.

## Unreleased — public wording, provenance and release assurance

### Added

- Added typed IEC 61850 UTC-Time forensic evidence exposing exact preserved wire bytes, the 24-bit fractional-second field, TimeQuality flags/accuracy, explicit UTC/local-offset views, and report timestamp-source separation.
- Added report timestamp evidence that keeps IED data timestamps, report `TimeOfEntry`, and client `ReceivedAt` as distinct timing concepts; exact `TimeOfEntry` wire evidence is linked when the original decoded InformationReport is supplied.
- Synthetic/programmatically created UTC-Time values remain usable but are explicitly reported as having no raw-wire provenance.

### Changed

- Corrected website structured-data licensing to `GPL-3.0-or-later`.
- Replaced stale active-license wording and milestone journals with current evidence and future-only roadmap documents.
- Standardized copyright identity as Ari Sulistiono, GitHub account `masarray`.
- Clarified the Contributor License Agreement parties, grant, and acceptance record.
- Aligned clean-room, contribution, security, operational-risk, and branding policies.
- Replaced broad safety and certification-sounding language with scoped protocol guardrails and evidence boundaries.
- Added automated checks for known stale public wording.
- Updated documentation to reflect persistent report monitoring and the current read-only simulator path.

### Fixed

- Preserved IEC 61850 UTC-Time sub-millisecond precision through the shared MMS renderer and report `TimeOfEntry` path instead of falling back to three fractional digits.
- Added one UTC-Time formatting policy with full seven-digit engine evidence and a five-digit engineering display option for dispute and event-analysis views.
- Preserved the exact 8-byte UTC-Time payload in decoded MMS values and reuse it for byte-exact re-encoding instead of reconstructing a potentially different fractional field from rounded .NET ticks.

## Previous unreleased work — Control and public release refresh

### Added

- Typed IEC 61850 client-side control-object service in `AR.Iec61850.Control`.
- Automatic detection and execution of Direct Operate and Select-Before-Operate, normal and enhanced security models.
- Typed live `ctlVal` binding for DPC, SPC, INC/ISC, BSC, APC, and validated raw implementation variants.
- Immutable sequence handling for `ctlNum`, `T`, origin, Test, interlock check, and synchrocheck.
- CommandTermination, LastApplError, ControlError, and AddCause decoding.
- SBO ownership, timeout, best-effort Cancel, association-loss cleanup, and concurrency guards.
- Guarded WPF control tester with OPEN/CLOSE intent, live status feedback, advanced origin/check settings, live-command arming, and command evidence.
- Laboratory IED validation record for one end-to-end control request path.
- Dedicated control-workflow website page and refreshed search/social metadata.

### Changed

- Repositioned the public README around the core IEC 61850 engineering use cases rather than internal milestone notes.
- Moved detailed capability history to documentation and changelog files.
- Improved GitHub Pages information architecture, accessibility, mobile layout, and technical metadata.
- Updated documentation navigation and public validation boundaries.

### Fixed

- Missing `System.IO` namespace in the WPF control tester.
- Expired SBO selection state returning `Rejected` instead of deterministic `TimedOut`.
- xUnit analyzer findings for collection assertions.
- Duplicate milestone sections in the previous README.

## Earlier engineering milestones

Earlier milestones include MMS association and discovery, reporting readiness and persistent monitoring, GOOSE and Sampled Values codecs and diagnostics, SCL engineering profiles, PCAP workflows, deterministic simulation, Windows raw-Ethernet transport, Sampled Values laboratory publishing, and engineering evidence export.

See [Engine Maturity Matrix](docs/ENGINE_MATURITY_MATRIX.md) for current evidence and [Roadmap](ROADMAP.md) for future work only.
