# Contributing to ARIEC61850

ARIEC61850 is a clean-room IEC 61850 stack. Contributions must preserve that
boundary.

## Clean-room rules

- Do not copy or translate restrictive-license source code into this repository.
- Do not paste source from commercial tools or decompiled binaries.
- Use external projects only as behavioral references, documentation pointers,
  or interoperability peers.
- Keep protocol code in `src/`.
- Keep tester app code in `apps/`.

## Development workflow

Before opening a pull request:

```powershell
dotnet build .\ARIEC61850.sln -c Release
dotnet test .\ARIEC61850.sln -c Release --no-build
```

Add or update tests for:

- protocol codec changes,
- SCL parsing behavior,
- publisher/subscriber state machines,
- malformed input handling,
- PCAP read/write behavior.

## Active network features

Any feature that sends traffic to a NIC must:

- require explicit adapter selection,
- have an in-memory test path,
- document safety limitations,
- avoid hidden background publishing,
- expose enough evidence for operator review.

## Contribution licensing and provenance

The public project is distributed under `GPL-3.0-or-later` and also maintains a separate commercial-licensing path.

Before a code contribution can be merged, the contributor must:

1. have the legal right to submit the contribution;
2. agree to [CONTRIBUTOR-LICENSE-AGREEMENT.md](CONTRIBUTOR-LICENSE-AGREEMENT.md), which preserves the maintainer's ability to offer both GPL and commercial licensing;
3. add a Developer Certificate of Origin sign-off (`Signed-off-by: Name <email>`) to every commit; and
4. avoid confidential customer data, employer-owned material, vendor source code, restrictive-license code, and mechanically translated proprietary implementations.

Organizational contributions must be submitted by a person authorized to bind the organization. A pull request without the CLA affirmation and DCO sign-off will not be merged.
