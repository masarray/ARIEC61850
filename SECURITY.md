# Security Policy

ARIEC61850 can generate and publish raw Ethernet process-bus traffic in lab
contexts. Treat every active publishing feature as safety-sensitive.

## Supported versions

The repository is pre-release. Security review applies to the `main` branch.

## Reporting a vulnerability

Open a private report through GitHub Security Advisories if available, or open a
minimal public issue that avoids exploit details and requests maintainer contact.

Please include:

- Commit hash.
- Operating system.
- .NET SDK version.
- Adapter and driver details when raw Ethernet behavior is involved.
- Minimal reproduction steps.

## Safety-sensitive areas

- Raw Ethernet publishing through Npcap.
- Future MMS write/control services.
- Future report-control and server simulation behavior.
- Any code path that can send traffic to a real network.

## Responsible use

Do not run active publishing on production substation networks. Use isolated lab
adapters, TAPs, or test switches.
