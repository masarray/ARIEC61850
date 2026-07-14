# Third-Party Notices

ARIEC61850 is distributed under `GPL-3.0-or-later`. The project license does **not** change the license of any third-party package, tool, sample, standard, or asset. Each third-party component remains subject to its own license and attribution terms.

## Direct build and test dependencies

The following packages may be restored by .NET during build or test:

| Dependency | Used by | Purpose | License |
|---|---|---|---|
| SharpPcap | `src/AR.Iec61850.Transports.Npcap` | Packet capture and raw Ethernet access wrapper for Npcap/libpcap-compatible environments | MIT |
| Microsoft.NET.Test.Sdk | `tests/AR.Iec61850.Tests` | .NET test infrastructure | MIT |
| xUnit | `tests/AR.Iec61850.Tests` | Unit testing framework | Apache-2.0 |
| xUnit runner visualstudio | `tests/AR.Iec61850.Tests` | Visual Studio and `dotnet test` integration | Apache-2.0 |
| coverlet.collector | `tests/AR.Iec61850.Tests` | Optional code coverage collection | MIT |

Npcap is not bundled in this repository. Users who need live raw Ethernet traffic on Windows must install it separately from its official distribution channel and comply with its license.

## External IEC 61850 protocol stacks

No source code, binary, generated binding, header, example, test, documentation fragment, or API wrapper from libiec61850 or any other external IEC 61850 protocol stack is included, linked, or required by ARIEC61850.

ARIEC61850 does not claim to be a port, fork, wrapper, derivative, drop-in replacement, or commercially licensed edition of libiec61850. The project uses its own C# namespaces, models, codecs, state machines, tests, and application architecture. Any standards overlap is functional interoperability required by IEC 61850 and related public protocol specifications, not a claim to third-party authorship or affiliation.

`libiec61850`, `MZ Automation`, and associated names are property of their respective owners. Their mention in legal or provenance records is nominative only. ARIEC61850 is not affiliated with, sponsored by, certified by, or endorsed by MZ Automation.

## Proprietary IEC 61850 engineering tools

No executable, library, manual, brochure, help file, screenshot, icon, logo, product photo, report template, text, UI resource, database, capture, or extracted asset from OMICRON products or other proprietary IEC 61850 engineering tools is bundled or licensed as part of this repository.

Commercial tools may be used separately by their lawful licensees for black-box interoperability testing. Such use does not make the tool a project dependency and does not authorize copying its software, documentation, visual design, resources, or confidential data. Protocol cases retained in this repository must be independently reconstructed from public standards or ARIEC61850's own encoders under `docs/CLEAN_ROOM_POLICY.md`.

`OMICRON`, `IEDScout`, `SVScout`, `StationScout`, and other third-party product names and marks belong to their respective owners. ARIEC61850 is not affiliated with, sponsored by, certified by, or endorsed by OMICRON electronics GmbH.

## Release review

Before every public or commercial release:

1. review the resolved dependency graph and license metadata;
2. confirm that no external IEC 61850 stack code or binary has entered the source or package;
3. confirm that no proprietary manual, screenshot, logo, UI asset, capture, SCL, or customer material is present;
4. preserve all required third-party notices and license copies; and
5. run the repository source-clean and release-package verification scripts.
