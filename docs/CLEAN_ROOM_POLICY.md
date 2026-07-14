# Clean-Room and Interoperability Policy

ARIEC61850 is maintained as an independently written IEC 61850 implementation. This policy protects the project from copyright, license, trade-secret, trademark, and contractual contamination while still allowing lawful standards-based interoperability testing.

## Permitted implementation sources

Implementation may be based only on:

- independently written code and engineering analysis;
- publicly available IEC, ISO, IETF, IEEE, and UCA specifications or other lawfully obtained standards material;
- public protocol descriptions, published errata, and vendor-neutral interoperability guidance;
- self-authored SCL, PCAP, fixtures, packet encoders, simulators, diagrams, documentation, and tests;
- black-box observations of standards-compliant network behavior produced by lawfully licensed equipment or software, provided the observation is reduced to vendor-neutral protocol facts and independently re-derived in code.

## Prohibited external-stack use

The repository must not contain, link to, translate, mechanically port, adapt, or derive implementation code from any external IEC 61850 protocol stack, including libiec61850 or its C, C#, Java, Python, examples, generated bindings, headers, binaries, documentation examples, or API structure.

Developers must not:

- copy source, comments, tests, examples, constants, type layouts, naming schemes, or error strings from an external stack;
- use decompilation, disassembly, symbol extraction, reflection, memory inspection, or binary comparison to recreate another implementation;
- use another stack as a code-generation or line-by-line translation source;
- reproduce a distinctive external API merely to claim compatibility unless an independently documented interoperability requirement makes that interface necessary and legal review approves it;
- commit external stack binaries, archives, generated wrappers, headers, or source fragments.

## Proprietary engineering-tool boundary

Proprietary IEC 61850 tools may be used only as lawfully licensed black-box interoperability clients or servers in an isolated laboratory. Their software, manuals, help content, screenshots, icons, logos, color systems, window layouts, report templates, wording, internal files, databases, scripts, captures, and other expressive assets must not be copied or used as design source material.

Specifically:

- no decompilation, disassembly, patching, database extraction, resource extraction, memory inspection, or circumvention of technical restrictions;
- no reproduction of proprietary UI composition, icons, screenshots, manuals, help pages, report layouts, or marketing text;
- no raw vendor-client packet dump may be committed as a permanent implementation fixture;
- a protocol behavior observed during interoperability testing must be re-created from the applicable public protocol grammar using ARIEC61850's own encoders and documented as a vendor-neutral standards case;
- product names may appear only where factually necessary in a legal audit, compatibility record, or nominative interoperability statement, never as branding, endorsement, implementation authority, or evidence of affiliation.

## Test-fixture provenance

Every protocol fixture committed to the repository must be one of:

1. emitted by ARIEC61850's own encoder;
2. manually constructed from a cited public protocol grammar;
3. generated from a synthetic, owner-created SCL model; or
4. reduced from lawful black-box observation to the minimum non-expressive protocol facts, then independently reconstructed and reviewed.

Fixtures must not include customer names, real station identifiers, serial numbers, credentials, proprietary SCL, confidential captures, vendor screenshots, or copied explanatory text. The review record should identify the public specification or independent derivation method.

## Repository and release boundaries

Not allowed:

- proprietary customer captures or confidential engineering data;
- generated build output, IDE state, release artifacts, runtime evidence, or third-party executables;
- copyrighted manuals, brochures, screenshots, logos, product photos, fonts, or extracted application resources;
- wording that implies another vendor authored, certified, endorsed, supplied, or is affiliated with this project;
- documentation that presents another IEC 61850 implementation or commercial product as the source of ARIEC61850 behavior.

Release packages may contain only project-owned material and properly licensed third-party dependencies listed in `THIRD_PARTY_NOTICES.md`.

## Independent user-interface rule

The user interface must be designed from ARIEC61850's own workflow requirements and visual system. Common functional concepts such as a model tree, signal table, event list, phasor plot, waveform plot, or protocol log may be implemented, but the selection, arrangement, visual treatment, labels, icons, artwork, and interaction details must be independently designed and must not imitate the overall look and feel of a proprietary product.

## Public wording and trademark rule

Public documentation should describe ARIEC61850 as:

- independently developed and clean-room;
- native C#/.NET;
- vendor-neutral and laboratory-oriented;
- GPL-3.0-or-later licensed;
- not affiliated with, sponsored by, or endorsed by MZ Automation, OMICRON, or any other third party;
- not formally conformance certified unless that becomes true for a specific release.

Third-party product and company names remain the property of their respective owners and may be used only for truthful nominative reference. Avoid competitor comparisons, copied feature descriptions, or wording that creates source, sponsorship, certification, or affiliation confusion.

## Review requirement

Any contribution involving externally observed protocol behavior, an imported capture, a compatibility workaround, a third-party asset, or a product-specific test must receive explicit provenance review before merge. When provenance cannot be demonstrated, the material must not enter the repository.
