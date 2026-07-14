# Third-Party Notices

The project license transition to GPL-3.0-or-later does **not** change the license of any third-party package, tool, sample, or asset. Each third-party component remains subject to its own license and attribution terms.

This repository is licensed under the Apache License 2.0. The following third-party packages may be restored by .NET during build or test.

| Dependency | Used by | Purpose | License |
|---|---|---|---|
| SharpPcap | `src/AR.Iec61850.Transports.Npcap` | Packet capture / raw Ethernet access wrapper for Npcap/libpcap-compatible environments | MIT |
| Microsoft.NET.Test.Sdk | `tests/AR.Iec61850.Tests` | .NET test infrastructure | MIT |
| xUnit | `tests/AR.Iec61850.Tests` | Unit testing framework | Apache-2.0 |
| xUnit runner visualstudio | `tests/AR.Iec61850.Tests` | Visual Studio / `dotnet test` integration | Apache-2.0 |
| coverlet.collector | `tests/AR.Iec61850.Tests` | Optional code coverage collection | MIT |

Npcap is not bundled in this repository. Users who need live raw Ethernet traffic on Windows should install Npcap separately from its official distribution channel and follow its license terms.

No external IEC 61850 protocol stack source code is included in this repository.
