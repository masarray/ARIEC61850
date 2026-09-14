# SCL Import / Export — Start Here

For any change involving **Open SCL**, **Save/Export SCL**, live-model reconstruction, IED-name inference, SCL type synthesis, Edition 2/Edition 1 conversion, IID/ICD output, or SCL-assisted online validation, read:

- **[`docs/SCL_IMPORT_NORMALIZATION_PROFILE.md`](docs/SCL_IMPORT_NORMALIZATION_PROFILE.md)** — Open SCL -> normalize -> canonical model -> multi-edition export.
- **[`docs/SCL_EXPORT_RECONSTRUCTION_PROFILE.md`](docs/SCL_EXPORT_RECONSTRUCTION_PROFILE.md)** — live MMS discovery -> canonical model -> multi-edition export.

Thirty-second contract:

```text
                    LIVE MMS DISCOVERY
                           |
                           v
                    typed live evidence
                           |
                           +-------------------+
                                               |
                                               v
                                      CANONICAL IED MODEL
                                               ^
                                               |
                           +-------------------+
                           |
                           v
                    SCL IMPORT/NORMALIZE
                           ^
                           |
                        OPEN SCL

CANONICAL IED MODEL
  identity + communication
  LD/LN/DO/DA/type semantics
  declared DataSets/RCBs/configuration
  optional live runtime overlay
  provenance + diagnostics
       |
       +--> Edition 2 Schema V3.1 -> IID
       +--> Edition 1 Schema V1.6 -> ICD
       +--> Edition 1 Schema V1.5 -> ICD
       `--> Edition 1 Schema V1.4 -> ICD
```

Do not forget:

- **One semantic model, multiple ingress paths.** Live discovery and Open SCL must converge before export.
- Save/export is a **local projection**; switching target edition must not rediscover the IED or require a live connection.
- Open SCL is a **typed semantic import**, not an XML DOM kept as application truth.
- `IED@name` is authoritative for the opened file; SCL-assisted online use must cross-check live identity and surface mismatches rather than silently rewriting the file-derived model.
- Declared SCL configuration is not proof of live runtime state. Keep structural/configuration evidence separate from current RCB ownership, EntryID, runtime DataSets, values and association state.
- Source and target editions belong in typed import/export profiles. Unknown/not-representable semantics must remain explicit instead of becoming false/default silently.
- Same-edition normalized round trip is judged by **semantic idempotence**, not byte equality.
- Original type IDs, descriptions, topology, Header/history and private extensions are source evidence. Preserve or diagnose them separately; do not make them a second semantic model.
- Live-discovery IED-name resolution remains evidence-scored; a worker/thread is only a responsiveness mechanism.
- Discovery-derived output may need deterministic synthetic type IDs because MMS cannot recover every original engineering identifier.
- Existing ARIEC61850 provenance, reporting and `ProductionEligible` rules remain stronger than SCL conversion convenience.

Related knowledge:

- [`AGENTS.md`](AGENTS.md)
- [`docs/SCL_ASSISTED_MMS_CONNECT_STEP1.md`](docs/SCL_ASSISTED_MMS_CONNECT_STEP1.md)
- [`docs/SCL_ASSISTED_MMS_CONNECT_STEP2.md`](docs/SCL_ASSISTED_MMS_CONNECT_STEP2.md)
- current Step 3 SCL-assisted live association work
- [`RCB_REPORTING.md`](RCB_REPORTING.md)
