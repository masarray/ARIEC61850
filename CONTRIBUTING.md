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
dotnet build .\ARIEC61850.slnx -c Release
dotnet test .\ARIEC61850.slnx -c Release --no-build
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
