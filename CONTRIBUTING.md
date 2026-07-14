# Contributing to ARIEC61850

ARIEC61850 is independently developed under a documented clean-room and provenance policy. Contributions must preserve that boundary.

## Before contributing

Read:

- `AGENTS.md`
- `docs/CLEAN_ROOM_POLICY.md`
- `CONTRIBUTOR-LICENSE-AGREEMENT.md`
- `DCO.txt`

## Permitted contribution sources

Contributions may be based on:

- lawfully obtained public standards and published protocol specifications;
- independently written engineering analysis;
- project-owned code, synthetic SCL, fixtures, captures, diagrams, and tests;
- lawful black-box interoperability observations reduced to vendor-neutral protocol facts and independently reconstructed using project-owned codecs.

External software may be used only as a lawfully licensed black-box interoperability endpoint. Do not use its source, API composition, examples, tests, documentation wording, UI, resources, or internal structure as implementation design material.

## Prohibited material

Do not submit:

- copied, translated, mechanically ported, or generated translations of another implementation;
- decompiled, disassembled, extracted, or reverse-engineered material;
- private SDK headers, generated wrappers, binaries, manuals, screenshots, icons, logos, report templates, or application resources;
- raw captures from an external client as permanent fixtures;
- customer, employer, station, credential, serial-number, or confidential project data;
- SCL, PCAP, screenshots, or fixtures whose provenance and redistribution rights are not documented;
- wording that implies certification, sponsorship, affiliation, universal interoperability, regulatory approval, or operational safety.

## Development workflow

Before opening a pull request:

```powershell
dotnet restore .\ARIEC61850.sln
dotnet build .\ARIEC61850.sln -c Release
dotnet test .\ARIEC61850.sln -c Release --no-build
.\scripts\verify-source-clean.cmd
```

Add or update tests for protocol codecs, state machines, SCL parsing, malformed input, cancellation, timeout, negative service results, and active-network guardrails where relevant.

Keep protocol logic in `src/`. Applications in `apps/` should consume typed engine services rather than duplicate parsing, binding, reporting, or control logic.

## Fixture provenance

A committed fixture must be:

1. emitted by ARIEC61850;
2. manually constructed from a cited public protocol grammar;
3. generated from a synthetic contributor-owned SCL model; or
4. independently reconstructed from the minimum non-expressive facts observed during lawful black-box testing.

Describe the origin and reconstruction method in the pull request. Do not commit real customer captures or project files.

## Active network features

Any feature that sends traffic must:

- require explicit adapter or endpoint selection;
- provide an in-memory or dry-run test path where practical;
- expose the target and operation before execution;
- require deliberate confirmation;
- document isolation, authority, and validation limitations;
- avoid hidden background publishing or control;
- preserve enough evidence for review.

Protocol guardrails do not prove that primary equipment or a switching procedure is safe.

## Claim wording

Use evidence-based wording:

- `implemented`
- `unit tested`
- `loopback verified`
- `laboratory exercised`
- `partial`
- `not validated`

Do not claim formal conformance, certification, broad interoperability, production timing, or operational readiness without evidence for the exact release.

## Contribution licensing

The public project is distributed under `GPL-3.0-or-later` and maintains a separate commercial-licensing path.

Before a code contribution can be merged, the contributor must:

1. have the legal right to submit it;
2. affirmatively agree to `CONTRIBUTOR-LICENSE-AGREEMENT.md` in the pull request;
3. add a Developer Certificate of Origin sign-off (`Signed-off-by: Name <email>`) to every commit; and
4. identify any third-party material and its license.

Organizational contributions must be submitted by a person authorized to grant the required rights. A pull request without CLA affirmation, DCO sign-off, and adequate provenance will not be merged.
