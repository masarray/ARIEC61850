# External IP Cleanliness Audit — 2026-07-14

This is a repository-evidence and process audit, not a legal opinion or a guarantee that no claim can ever be made.

## Scope

The audit reviewed the current ARIEC61850 source tree and available Git history for contamination risk associated with unrelated external implementations, proprietary engineering products, copied code, API structures, manuals, screenshots, visual assets, report layouts, raw captures, binaries, and misleading affiliation claims.

## Current-tree findings

At the audit date:

- no unrelated external protocol-stack source, binary, header, generated binding, wrapper, namespace, package reference, or direct API identifier was detected in the current source tree;
- no unrelated external protocol-stack dependency was listed in the direct .NET package references;
- no proprietary executable, library, manual, brochure, screenshot, icon, logo, product photo, report template, or extracted application resource was detected in the tracked repository;
- no current implementation or public documentation file identifies an external commercial product as an implementation source or dependency;
- the project implementation is organized under independent `AR.Iec61850` C# namespaces and project-owned codecs, models, services, state machines, applications, and tests;
- the repository license audit found no external human code contributor whose copyright permission must be obtained for the present licensing model.

These findings support an independently developed clean-room position, but automated scans cannot prove the absence of all conceptual similarity or undisclosed off-repository material.

## Historical reference review

Earlier development records contained product-specific laboratory wording. Current source wording is vendor-neutral, and new tests must describe public protocol forms and independent derivation rather than presenting any proprietary tool as implementation authority.

Git history has not been rewritten because the audit found no copied external source or proprietary asset requiring removal. If later evidence shows that protected material entered history, preserve evidence, stop distribution of the affected revision, obtain legal advice, and perform a targeted history purge.

## External implementation boundary

ARIEC61850 must not copy, translate, mechanically port, wrap, link, redistribute, or imitate source, binaries, headers, bindings, examples, tests, comments, documentation, naming schemes, or API layouts from unrelated external implementations.

Standards-compliant behavior shared by independent implementations is not evidence of copying by itself. The defensible record is independent code, standards-based design notes, self-authored fixtures, and documented clean-room review.

## Proprietary-tool boundary

Proprietary engineering products and their manuals, screenshots, icons, logos, UI composition, report layouts, help text, marketing language, internal files, and software resources must be treated as protected external material.

Lawfully licensed black-box interoperability testing is permitted only under the applicable license and organizational policy. It must not involve decompilation, disassembly, resource extraction, database inspection, memory inspection, technical-restriction circumvention, or copying of protected expression.

When an external client exposes a protocol behavior:

1. record only the minimum vendor-neutral protocol fact;
2. verify that fact against a public protocol grammar or independent second implementation where practicable;
3. reconstruct the request or response using ARIEC61850's own encoders;
4. commit only the reconstructed synthetic fixture, not the raw external capture; and
5. document the derivation without copied manual text or screenshots.

## User-interface and feature boundary

Functional engineering concepts such as model browsing, signal tables, event lists, waveform plots, phasor diagrams, SCL views, report monitoring, and test workflows are common in the field. Copyright-sensitive risk arises from copying the particular selection, arrangement, artwork, wording, icons, visual hierarchy, interaction sequence, or overall presentation of another product.

ARIEC61850 applications must retain an independently designed visual system and workflow. External product screenshots must not be used as UI specifications, design templates, website assets, release illustrations, or documentation backgrounds.

## Controls

- maintain an explicit clean-room and interoperability policy;
- maintain dependency notices only for components actually used by the project;
- reject external-product identifiers from normal source and documentation through fingerprint-based scanning;
- reject tracked binaries, captures, manuals, build artifacts, and confidential project data;
- review dependencies, attribution, and release-package contents before distribution.

## Remaining manual checks

Before a commercial OEM, enterprise, or white-label agreement, manually review:

- applicable software license and account/download conditions governing laboratory use;
- employment, invention-assignment, moonlighting, confidentiality, and customer-data obligations;
- whether any private design note, screenshot, packet capture, SCL, support response, or test artifact originated from a customer, employer, vendor portal, training course, or licensed manual;
- source-code similarity against any implementation a developer had access to, using counsel-directed methods when material commercial value is involved;
- trademark clearance for ARIEC61850 and application branding; and
- the resolved licenses and notices of all packaged dependencies.

## Conclusion

No current repository evidence was found that ARIEC61850 contains or depends on unrelated external implementation code or bundles proprietary software, manuals, screenshots, branding, or UI assets. The principal residual risk is off-repository provenance and contractual restrictions that repository inspection cannot establish. The controls above materially reduce copyright and affiliation risk but do not replace professional legal review for a high-value commercial transaction.
