# Clean-Room Policy

ARIEC61850 is maintained as an independent clean-room IEC 61850 implementation.

## Repository boundaries

Allowed:

- implementation based on public standards understanding, interoperability behavior, and independently written code;
- self-authored protocol codecs, models, diagnostics, CLI tools, UI code, docs, and tests;
- public sample SCL files created for demonstration or anonymized validation.

Not allowed:

- copying source code from external IEC 61850 protocol stacks;
- committing proprietary customer captures or confidential engineering data;
- committing generated build output, IDE state, release artifacts, or runtime evidence;
- adding documents that make the project appear dependent on another IEC 61850 implementation.

## Public wording rule

Public documentation should describe ARIEC61850 as:

- clean-room;
- native C#/.NET;
- lab-oriented;
- Apache-2.0 licensed;
- not formally conformance certified unless that becomes true for a specific release.

Avoid internal audit language, competitor comparisons, or wording that creates unnecessary legal ambiguity.
