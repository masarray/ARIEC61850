# AI / Engineer Knowledge Index — Read Before Protocol Changes

This repository contains protocol behavior reconstructed from standards-facing engineering, project-owned tests, and controlled vendor-neutral black-box interoperability evidence. Do not re-derive these areas from memory or from one packet trace.

Read the topic contract before changing the corresponding code:

- **Project engineering/provenance rules:** [`AGENTS.md`](AGENTS.md).
- **RCB reporting, URCB/BRCB activation, report bit semantics, replay/recovery:** [`RCB_REPORTING.md`](RCB_REPORTING.md).
- **Open SCL, live MMS reconstruction, canonical normalization, IED-name resolution, and Edition 2 / Edition 1 export:** [`SCL_EXPORT.md`](SCL_EXPORT.md), then [`docs/SCL_IMPORT_NORMALIZATION_PROFILE.md`](docs/SCL_IMPORT_NORMALIZATION_PROFILE.md) and [`docs/SCL_EXPORT_RECONSTRUCTION_PROFILE.md`](docs/SCL_EXPORT_RECONSTRUCTION_PROFILE.md).
- **SCL-assisted MMS association planning/runtime:** [`docs/SCL_ASSISTED_MMS_CONNECT_STEP1.md`](docs/SCL_ASSISTED_MMS_CONNECT_STEP1.md), [`docs/SCL_ASSISTED_MMS_CONNECT_STEP2.md`](docs/SCL_ASSISTED_MMS_CONNECT_STEP2.md), and current Step 3 work.

For SCL import/export, the non-negotiable architecture is:

```text
LIVE DISCOVERY --------+
                        |
                        v
                 CANONICAL IED MODEL
                        ^
                        |
OPEN SCL -> NORMALIZE --+
                        |
                        v
              EDITION EXPORT PROFILES
```

The same canonical model must feed offline Open-SCL workflows, SCL-assisted online validation, and normalized multi-edition Save SCL.

Do **not** create separate semantic models or exporters for discovered IEDs and opened SCL files. Do **not** keep an XML DOM as the protocol source of truth. Do **not** create separate discovery algorithms per schema. Do **not** make IED-name accuracy depend on a UI worker/thread. Do **not** use string-replacement hacks for Edition conversion. Do **not** silently convert unknown/unrepresentable semantics to false/default. Do **not** claim a normalized reconstruction is the original vendor engineering file.

The detailed evidence, round-trip rules, provenance boundaries, transformation strategy, and acceptance invariants are in the two SCL profile documents linked above.
