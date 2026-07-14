# Clean-Room and Interoperability Policy

ARIEC61850 is maintained as an independently written IEC 61850 implementation. This policy protects the project from copyright, license, trade-secret, trademark, and contractual contamination while allowing lawful standards-based interoperability testing.

## Permitted implementation sources

Implementation may be based only on:

- independently written code and engineering analysis;
- lawfully obtained public standards and published protocol specifications;
- public errata and vendor-neutral interoperability guidance;
- self-authored SCL, protocol fixtures, packet encoders, simulators, diagrams, documentation, and tests;
- black-box observations of standards-compliant behavior, provided each observation is reduced to vendor-neutral protocol facts and independently re-derived in project-owned code.

## Prohibited external implementation use

The repository must not contain, link to, translate, mechanically port, adapt, or derive implementation code from any external protocol stack or proprietary engineering product.

Developers must not:

- copy source, comments, tests, examples, constants, type layouts, naming schemes, error strings, or API composition from another implementation;
- use decompilation, disassembly, symbol extraction, reflection, memory inspection, resource extraction, or binary comparison to recreate another implementation;
- use another implementation as a code-generation or line-by-line translation source;
- reproduce a distinctive external API merely to claim compatibility;
- commit external binaries, archives, generated wrappers, headers, source fragments, manuals, screenshots, logos, icons, report templates, or extracted resources.

## Proprietary engineering-tool boundary

Proprietary tools may be used only as lawfully licensed black-box interoperability participants in an isolated laboratory. Their software, manuals, help content, screenshots, artwork, visual systems, window layouts, report templates, wording, internal files, databases, scripts, captures, and other expressive assets must not be copied or used as design source material.

A protocol behavior observed during interoperability testing must be reconstructed from the applicable public protocol grammar using project-owned encoders and documented as a vendor-neutral standards case. Raw external-client packet dumps must not be committed as permanent fixtures.

## Test-fixture provenance

Every protocol fixture committed to the repository must be one of:

1. emitted by ARIEC61850's own encoder;
2. manually constructed from a cited public protocol grammar;
3. generated from a synthetic, owner-created SCL model; or
4. reduced from lawful black-box observation to minimum non-expressive protocol facts, then independently reconstructed and reviewed.

Fixtures must not include customer names, real station identifiers, serial numbers, credentials, proprietary SCL, confidential captures, external screenshots, or copied explanatory text.

## Repository and release boundaries

Not allowed:

- proprietary customer captures or confidential engineering data;
- generated build output, IDE state, runtime evidence, or unrelated executables;
- copyrighted manuals, brochures, screenshots, logos, product photos, fonts, or extracted application resources;
- wording that implies an external party authored, certified, endorsed, supplied, or is affiliated with this project;
- documentation that presents another implementation or commercial product as the source of ARIEC61850 behavior.

Release packages may contain only project-owned material and properly licensed dependencies listed in `THIRD_PARTY_NOTICES.md`.

## Independent user-interface rule

The user interface must be designed from ARIEC61850's own workflow requirements and visual system. Common functional concepts may be implemented, but their selection, arrangement, visual treatment, labels, icons, artwork, and interaction details must be independently designed and must not imitate the overall presentation of another product.

## Public wording rule

Public documentation should describe ARIEC61850 as independently developed, clean-room, native C#/.NET, vendor-neutral, laboratory-oriented, and not formally conformance certified unless that becomes true for a specific release.

Avoid competitor comparisons, copied feature descriptions, external product names, and wording that creates source, sponsorship, certification, or affiliation confusion.

## Review requirement

Any contribution involving externally observed protocol behavior, an imported capture, a compatibility workaround, an external asset, or a product-specific test requires explicit provenance review before merge. When provenance cannot be demonstrated, the material must not enter the repository.
