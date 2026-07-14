# License and Provenance Audit — 2026-07-14

This is a repository-evidence review prepared for the transition from the historical license to `GPL-3.0-or-later` with a separate commercial-licensing path. It is not a legal opinion and does not resolve off-repository ownership obligations.

## Scope

- Repository: `masarray/ARIEC61850`
- Audited base revision: `d61a83f5b04e7bd2b847174eeac7f4f6e81ee8e1`
- Historical branch: `archive/apache-2.0-final`
- Commit count visible at the audit date: 115
- Full-depth history inspected in GitHub Actions
- Pull-request identities queried when available
- Direct package references and tracked binary-like files scanned

## Git author identities observed

- `dependabot[bot]`
- `github-actions[bot]`
- `masarray`

The human contribution activity visible to this audit was attributable to the `masarray` account. Attribution to an account is evidence of repository activity; it is not by itself a legal conclusion about copyright ownership.

## Pull-request identities observed

- `dependabot[bot]`
- `masarray`

No external human pull-request author was detected in the accessible history. Automated dependency-update accounts were treated as automation rather than independent human copyright contributors.

## Co-author trailers

None were detected in the audited history.

## Direct package references

- `coverlet.collector` — test coverage
- `Microsoft.NET.Test.Sdk` — test infrastructure
- `SharpPcap` — packet capture and raw Ethernet integration
- `xunit` — unit testing
- `xunit.runner.visualstudio` — test runner integration

Package presence alone does not establish license compatibility. `THIRD_PARTY_NOTICES.md` must remain current, and resolved dependency licenses must be reviewed before each release intended for commercial distribution.

## Tracked binary, archive, and capture scan

No tracked compiled, archive, or packet-capture file was detected by the audit extension scan.

## Repository conclusion

The accessible history supports a maintainer-controlled licensing transition for the audited contribution set because no external human contributor was identified. This conclusion is limited to repository evidence and does not determine the effect of employment, invention-assignment, contractor, customer, or confidentiality obligations.

The historical revision remains available on the archive branch under its original terms. The current `main` branch and current release packages identify `GPL-3.0-or-later` as the community license.

## Unresolved off-repository checks

Before relying on commercial enforcement or signing a high-value commercial agreement, review:

- employment, invention-assignment, moonlighting, and contractor clauses;
- whether any work was created using employer time, equipment, confidential information, or customer material;
- provenance and redistribution rights for every SCL, PCAP, screenshot, fixture, font, icon, and other asset;
- compatibility and attribution requirements of every distributed dependency;
- contributor acceptance records;
- trademark availability and registration status; and
- the identity of the legal person or entity signing the commercial agreement.

Retain independent-creation evidence, commit history, design notes, provenance records, contributor acceptance records, and copies of relevant agreements.
