# License and Provenance Audit — 2026-07-14

This is a repository-evidence audit prepared for the Apache-2.0 → GPL-3.0-or-later plus commercial-licensing transition. It is not a legal opinion.

## Scope

- Repository: `masarray/ARIEC61850`
- Audited base revision: `d61a83f5b04e7bd2b847174eeac7f4f6e81ee8e1`
- Historical branch created: `archive/apache-2.0-final`
- Commit count visible to the audit: 115
- Git history fetched with full depth in GitHub Actions
- Pull-request authors queried from the GitHub API when available
- Direct NuGet references and tracked binary-like files scanned

## Git author identities

- `dependabot[bot] <49699333+dependabot[bot]@users.noreply.github.com>`
- `github-actions[bot] <41898282+github-actions[bot]@users.noreply.github.com>`
- `masarray <ari.sulistiono@gmail.com>`

## Pull-request author identities

- `dependabot[bot]`
- `masarray`

### External human PR author finding

- No external human PR authors detected; automated dependency-update accounts are not treated as human code contributors.

## Co-author trailers

- None detected

## Direct package references

- coverlet.collector 10.0.1 — `tests/AR.Iec61850.Tests/AR.Iec61850.Tests.csproj`
- Microsoft.NET.Test.Sdk 18.7.0 — `tests/AR.Iec61850.Tests/AR.Iec61850.Tests.csproj`
- SharpPcap 6.3.1 — `src/AR.Iec61850.Transports.Npcap/AR.Iec61850.Transports.Npcap.csproj`
- xunit 2.9.3 — `tests/AR.Iec61850.Tests/AR.Iec61850.Tests.csproj`
- xunit.runner.visualstudio 3.1.5 — `tests/AR.Iec61850.Tests/AR.Iec61850.Tests.csproj`

Package presence alone does not establish license compatibility. `THIRD_PARTY_NOTICES.md` must remain current, and dependency licenses must be rechecked before each commercial release.

## Tracked binary/archive/capture scan

- No tracked compiled/archive/capture files detected by extension

## Repository conclusion

The accessible repository history supports a maintainer-controlled license transition: human pull-request activity is owned by the `masarray` account, while automated dependency updates are identifiable as automation. The historical Apache revision has been preserved before applying the new license.

## Unresolved off-repository checks

The following cannot be proven from GitHub and remain manual blockers before signing a high-value commercial agreement:

- employment agreement, invention-assignment, and moonlighting clauses;
- whether any work was created using employer time, equipment, confidential information, or customer material;
- whether sanitized SCL/PCAP samples or screenshots contain third-party rights;
- whether every third-party dependency and bundled asset is compatible with the intended distribution;
- trademark availability and registration status.

Keep independent-creation evidence, commit history, design notes, clean-room records, and copies of the relevant employment/IP agreements.
