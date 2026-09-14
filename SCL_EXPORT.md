# Live MMS -> SCL Export — Start Here

For any change involving **Save/Export SCL**, live-model reconstruction, IED-name inference, SCL type synthesis, Edition 2/Edition 1 downgrade, IID/ICD output, or export of live DataSet/RCB state, read:

**[`docs/SCL_EXPORT_RECONSTRUCTION_PROFILE.md`](docs/SCL_EXPORT_RECONSTRUCTION_PROFILE.md)**

Thirty-second contract:

```text
LIVE MMS DISCOVERY
       |
       v
CANONICAL LIVE IED MODEL
  identity + LD/LN/DO/DA/types
  communication context
  DataSets + RCB runtime snapshot
       |
       v
CACHE ONE SEMANTIC MODEL
       |
       +--> Edition 2 Schema V3.1 -> IID
       +--> Edition 1 Schema V1.6 -> ICD
       +--> Edition 1 Schema V1.5 -> ICD
       `--> Edition 1 Schema V1.4 -> ICD
```

Do not forget:

- Save/export is a **local projection** of an already discovered model; it must not rediscover the IED merely because another schema was selected.
- IED-name resolution is an **evidence-scored semantic resolver**, not a background-worker trick and not a blind `longestCommonPrefix()` call.
- A live export represents the **current discovered MMS model**, including currently visible DataSets and RCB bindings when they were discovered.
- The same canonical semantic model may serialize differently by edition: e.g. `ObjRef` vs compatible string form, `EntryID` vs compatible octet form, `SE` vs older compatible FC, and edition-specific report/service attributes.
- Edition compatibility belongs in a typed profile/transformer, not ad-hoc XML string replacement.
- MMS cannot recover every engineering-only detail from the original SCL. Synthetic deterministic type IDs and explicit provenance are required.
- Capture-specific counts, names, timings, and lexical quirks are evidence, not universal IEC 61850 constants.

Related knowledge:

- [`AGENTS.md`](AGENTS.md)
- [`docs/SCL_ASSISTED_MMS_CONNECT_STEP1.md`](docs/SCL_ASSISTED_MMS_CONNECT_STEP1.md)
- [`docs/SCL_ASSISTED_MMS_CONNECT_STEP2.md`](docs/SCL_ASSISTED_MMS_CONNECT_STEP2.md)
- [`RCB_REPORTING.md`](RCB_REPORTING.md)
