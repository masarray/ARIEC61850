# External IP and Provenance Review — 2026-07-14

This is a repository-evidence and process review. It is not a legal opinion, certification, or guarantee that no claim can ever be made.

## Scope

The review examined the current source tree and accessible Git history for risks involving:

- copied or mechanically translated implementation code;
- external API structures, examples, tests, comments, or documentation wording;
- manuals, screenshots, icons, logos, visual assets, reports, and application resources;
- raw external-client captures and confidential engineering data;
- misleading authorship, certification, sponsorship, or affiliation claims;
- direct dependencies on unrelated IEC 61850 implementations.

## Current repository findings

At the review date:

- no unrelated protocol-stack source, binary, header, generated binding, wrapper, or direct package dependency was detected in the current tree;
- no proprietary executable, manual, brochure, screenshot, icon, logo, product photo, report template, or extracted application resource was detected in tracked content;
- the implementation uses project-owned C# namespaces, codecs, models, services, state machines, applications, and tests;
- no external human contributor was identified in the accessible repository history beyond automated dependency-update activity;
- tracked binary-like and packet-capture extensions were not detected by the repository audit.

These findings support an independent-development position for the inspected repository. Automated scans cannot establish the provenance of undisclosed off-repository material or prove the absence of all conceptual similarity.

## Independent-development boundary

ARIEC61850 must not copy, translate, mechanically port, wrap, link, redistribute, or imitate source, binaries, headers, bindings, examples, tests, comments, documentation wording, naming schemes, or distinctive API layouts from unrelated implementations.

Shared standards-compliant behavior is not evidence of copying by itself. The defensible record is independently authored code, standards-based design notes, project-owned codecs, synthetic fixtures, deterministic tests, and documented provenance review.

## Lawful black-box interoperability

Separately licensed software may be used only as a black-box interoperability participant under the applicable license and organizational policy. Such use must not involve decompilation, disassembly, resource extraction, memory inspection, database extraction, technical-restriction circumvention, or copying of protected expression.

When an external endpoint reveals protocol behavior:

1. record only the minimum non-expressive protocol fact;
2. verify it against a public protocol grammar where practicable;
3. reconstruct the request or response using project-owned codecs;
4. commit only a synthetic minimal fixture;
5. document lawful provenance and review.

Raw external-client captures should not become permanent public fixtures.

## User-interface and documentation boundary

Common engineering concepts may be implemented, including model browsing, signal tables, event lists, waveform plots, phasor diagrams, SCL views, report monitoring, and test workflows. Layout, artwork, icons, wording, visual hierarchy, interaction details, reports, screenshots, and marketing presentation must be independently designed.

Public documentation must not present another implementation or commercial product as the source of ARIEC61850 behavior or imply certification, sponsorship, affiliation, or approval.

## Controls

The repository maintains:

- a clean-room and interoperability policy;
- contributor provenance and licensing requirements;
- dependency notices for components actually used;
- source-clean checks for prohibited identifiers, captures, binaries, manuals, and internal paths;
- release-package checks and claim boundaries;
- explicit separation between current GPL community licensing and historical licensing.

## Remaining manual checks

Before a commercial OEM, enterprise, or white-label agreement, manually review:

- applicable software licenses governing laboratory interoperability testing;
- employment, invention-assignment, contractor, confidentiality, and customer-data obligations;
- private design notes, screenshots, captures, SCL, support responses, and training material not visible in GitHub;
- contributor acceptance records;
- dependency licenses and packaged notices;
- trademark clearance and commercial-contract identity; and
- source similarity using counsel-directed methods when transaction value warrants it.

## Conclusion

No current repository evidence was found that ARIEC61850 contains unrelated implementation code or bundles proprietary software, documentation, branding, or UI assets. The principal residual risk is off-repository provenance and contractual restrictions that repository inspection cannot establish. The documented controls materially strengthen the independent-development record but do not replace professional legal review for a significant commercial transaction.
