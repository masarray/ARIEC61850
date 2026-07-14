# Third-Party Notices

ARIEC61850 is distributed under `GPL-3.0-or-later`. The project license does **not** change the license of any third-party package, standard, or asset. Each included or restored component remains subject to its own license and attribution terms.

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

## External intellectual-property boundary

No source code, binary, generated binding, header, example, test, documentation fragment, API wrapper, manual, screenshot, icon, logo, report template, database, capture, or extracted resource from an unrelated external implementation or proprietary engineering product is included or required by ARIEC61850.

Interoperability testing with separately licensed tools does not make those tools project dependencies and does not authorize copying their software, documentation, visual design, resources, or confidential data. Protocol cases retained in this repository must be independently reconstructed from public standards or ARIEC61850's own encoders under `docs/CLEAN_ROOM_POLICY.md`.

## Release review

Before every public or commercial release:

1. review the resolved dependency graph and license metadata;
2. confirm that no unrelated external implementation code or binary has entered the source or package;
3. confirm that no proprietary manual, screenshot, logo, UI asset, capture, SCL, or customer material is present;
4. preserve all legally required dependency notices and license copies; and
5. run the repository source-clean and release-package verification scripts.
