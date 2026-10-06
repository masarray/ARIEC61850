# SCL-assisted MMS connect — Step 2

Status: **implemented as a pure association-plan/encoder path with deterministic golden-byte tests; not wired to live sockets**.

## Purpose

Step 2 converts the typed SCL communication context from Step 1 into deterministic COTP, ISO Session, ISO Presentation, ACSE and MMS-Initiate request bytes without changing the current live client runtime.

The design keeps remote/called and local/calling identity separate:

- remote/called IP, TSEL, SSEL, PSEL, AP-title and AE qualifier come from the selected SCL `ConnectedAP`;
- local/calling selectors, AP-title, AE qualifier and MMS Initiate limits come from an explicit named client profile;
- the **exact-plan builder** still fails closed when required remote values are missing or invalid; later runtime interoperability resolution is a separate typed layer and never rewrites the source SCL;
- no remote value is silently replaced with a local value.

## Implemented

- parameterized `CotpConnectRequest.Build(CotpConnectParameters)` while preserving `BuildDefault()` byte-for-byte;
- pure `AcseMmsAssociationRequestBuilder` for Session/Presentation/ACSE/MMS Initiate encoding;
- typed `MmsLocalAssociationProfile` and `SclAssistedMmsAssociationPlan`;
- exact SCL selector/AP-title parsing and validation;
- `ExistingRuntimeDefault` as an explicit compatibility baseline, not an automatic fallback;
- golden test proving the parameterized encoder reproduces the existing `BalancedApTitle` association payload exactly when supplied the same identities;
- negative tests proving the exact builder does not silently invent incomplete SCL association addressing.

The exact builder is intentionally preserved as the deterministic complete-SCL contract. Step 3 now layers a bounded candidate resolver above it: genuinely unspecified fields may be resolved from engine-owned association profiles, while malformed or conflicting explicit declarations still fail closed.

## Runtime boundary

Step 2 does **not**:

- call `TpktClient`, `CotpClient`, or open TCP port 102;
- change `MmsClientSession.ConnectAsync`;
- select the new plan automatically;
- perform discovery, reads, writes, controls, report enable, or DataSet mutation.

The existing runtime continues to use `CotpConnectRequest.BuildDefault()` and the existing association profiles.

## Runtime handoff

Step 3 consumes either a complete exact plan or an engine-owned bounded association resolution, performs TCP/COTP/ACSE association, and then validates only the live MMS domain inventory with `GetNameList(Domain, VMD)`. It does not repeat NamedVariable/GVAA/DataSet discovery when the trusted SCL model is being used.
