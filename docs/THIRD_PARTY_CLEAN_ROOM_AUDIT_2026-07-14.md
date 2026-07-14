# Third-Party Clean-Room Audit — 2026-07-14

This is a repository-evidence and process audit, not a legal opinion or a guarantee that no claim can ever be made.

## Scope

The audit reviewed the current ARIEC61850 source tree and available Git history for legal contamination risk associated with:

- external IEC 61850 protocol stacks, especially libiec61850 and related MZ Automation APIs;
- proprietary IEC 61850 engineering products, especially OMICRON IEDScout, SVScout, and StationScout;
- copied code, API structures, manuals, screenshots, visual assets, report layouts, raw captures, binaries, and misleading affiliation claims.

## Current-tree findings

At the audit date:

- no libiec61850 source, binary, header, generated binding, wrapper, namespace, package reference, or direct API identifier was detected in the current source tree;
- no external IEC 61850 protocol-stack dependency was listed in the direct .NET package references;
- no OMICRON executable, library, manual, brochure, screenshot, icon, logo, product photo, report template, or extracted application resource was detected in the tracked repository;
- no current implementation or public documentation file identified IEDScout, SVScout, or StationScout as an implementation source or dependency;
- the project implementation is organized under independent `AR.Iec61850` C# namespaces and project-owned codecs, models, services, state machines, applications, and tests;
- the repository license audit found no external human code contributor whose copyright permission must be obtained for the present licensing transition.

These findings support an independently developed clean-room position, but automated name and dependency scans cannot prove the absence of all conceptual similarity or undisclosed off-repository material.

## Historical reference review

Historical commit messages and earlier comments used IEDScout as the name of a laboratory interoperability client. The relevant implementation changes constructed MMS BER messages with ARIEC61850's own `BerWriter` and implemented standard MMS ObjectName and DataSet response forms.

A historical product name in a commit message is a nominative factual reference and is not, by itself, copied program code. Nevertheless, current source wording has been made vendor-neutral, and new tests must describe the public protocol form and independent derivation rather than presenting a proprietary tool as the authority.

Git history has not been rewritten because the audit found no copied vendor source or proprietary asset requiring removal, and destructive history rewriting would weaken provenance evidence. If later evidence shows that protected material entered history, preserve evidence, stop distribution of the affected revision, obtain legal advice, and perform a targeted history purge.

## libiec61850 boundary

libiec61850 is a separate IEC 61850 implementation with its own GPL and commercial licensing model. ARIEC61850 must not:

- copy, translate, mechanically port, wrap, link, or redistribute its source, binaries, headers, bindings, examples, tests, comments, documentation, naming schemes, or API layout;
- claim libiec61850 compatibility by reproducing its API;
- use its implementation as a line-by-line reference during ARIEC61850 development;
- imply affiliation, endorsement, certification, sponsorship, or commercial authorization by MZ Automation.

Standards-compliant behavior shared by independent implementations is not evidence of copying by itself. The defensible record is independent code, standards-based design notes, self-authored fixtures, and documented clean-room review.

## OMICRON proprietary-tool boundary

IEDScout, SVScout, and StationScout are proprietary products and their manuals, screenshots, icons, logos, UI composition, report layouts, help text, marketing language, internal files, and software resources must be treated as protected third-party material.

Lawfully licensed black-box interoperability testing is permitted only under the applicable product license and organizational policy. It must not involve decompilation, disassembly, resource extraction, database inspection, memory inspection, technical-restriction circumvention, or copying of protected expression.

When a proprietary client exposes a protocol behavior:

1. record only the minimum vendor-neutral IEC/ISO protocol fact;
2. verify that fact against a public protocol grammar or independent second implementation where practicable;
3. reconstruct the request or response using ARIEC61850's own encoders;
4. commit only the reconstructed synthetic fixture, not the raw vendor capture;
5. document the derivation without copied manual text or screenshots.

## User-interface and feature boundary

Functional engineering concepts such as model browsing, signal tables, event lists, waveform plots, phasor diagrams, SCL views, report monitoring, and test workflows are common in the field. The copyright-sensitive risk is copying the particular selection, arrangement, artwork, wording, icons, visual hierarchy, interaction sequence, or overall presentation of a proprietary product.

ARIEC61850 applications must retain an independently designed visual system and workflow. Product screenshots must not be used as UI specifications, design templates, website assets, release illustrations, or documentation backgrounds.

## Trademark and marketing boundary

Third-party names may be used only where factually necessary to identify a separate product during legal, compatibility, or interoperability discussion. Releases and marketing must not use third-party logos, confusingly similar product names, comparative claims lacking evidence, or wording that suggests certification, partnership, sponsorship, or endorsement.

Recommended public statement:

> ARIEC61850 is an independently developed, vendor-neutral IEC 61850 toolkit. It is not affiliated with, sponsored by, certified by, or endorsed by MZ Automation, OMICRON, or any other IEC 61850 tool vendor.

## Controls added by this audit

- expanded `docs/CLEAN_ROOM_POLICY.md` with explicit external-stack, proprietary-tool, UI, fixture, and trademark boundaries;
- expanded `THIRD_PARTY_NOTICES.md` with non-dependency and non-affiliation statements;
- hardened `scripts/verify-source-clean.ps1` to reject external-stack and proprietary-tool names or files outside reviewed legal records;
- retained the existing ban on binaries, captures, build artifacts, and confidential project data;
- retained release review of dependencies, attribution, and source-package contents.

## Remaining manual checks

Before a commercial OEM, enterprise, or white-label agreement, manually review:

- every applicable OMICRON or other proprietary-tool EULA and account/download condition governing laboratory use;
- employment, invention-assignment, moonlighting, confidentiality, and customer-data obligations;
- whether any private design note, screenshot, packet capture, SCL, support response, or test artifact originated from a customer, employer, vendor portal, training course, or licensed manual;
- source-code similarity against any implementation that a developer had access to, using counsel-directed methods when material commercial value is involved;
- trademark clearance for ARIEC61850 and application branding;
- the resolved licenses and notices of all packaged third-party dependencies.

## Conclusion

No current repository evidence was found that ARIEC61850 contains or depends on libiec61850 code, or that it bundles OMICRON software, manuals, screenshots, branding, or UI assets. The principal residual risk is off-repository provenance and contractual restrictions that GitHub cannot establish. The clean-room controls above materially reduce copyright and affiliation risk but do not replace professional legal review for a high-value commercial transaction.
