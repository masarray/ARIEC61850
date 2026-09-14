# SCL-assisted MMS connect — Step 2

Status: **implemented as a pure association-plan/encoder path with deterministic golden-byte tests; not wired to live sockets**.

## Purpose

Step 2 converts the typed SCL communication context from Step 1 into deterministic COTP, ISO Session, ISO Presentation, ACSE and MMS-Initiate request bytes without changing the current live client runtime.

The design keeps remote/called and local/calling identity separate:

- remote/called IP, TSEL, SSEL, PSEL, AP-title and AE qualifier come from the selected SCL `ConnectedAP`;
- local/calling selectors, AP-title, AE qualifier and MMS Initiate limits come from an explicit named client profile;
- missing or invalid remote SCL association values fail closed and remain visible as typed build errors;
- no remote value is silently replaced with a local value.

## Implemented

- parameterized `CotpConnectRequest.Build(CotpConnectParameters)` while preserving `BuildDefault()` byte-for-byte;
- pure `AcseMmsAssociationRequestBuilder` for Session/Presentation/ACSE/MMS Initiate encoding;
- typed `MmsLocalAssociationProfile` and `SclAssistedMmsAssociationPlan`;
- exact SCL selector/AP-title parsing and validation;
- `ExistingRuntimeDefault` as an explicit compatibility baseline, not an automatic fallback;
- golden test proving the parameterized encoder reproduces the existing `BalancedApTitle` association payload exactly when supplied the same identities;
- negative tests proving incomplete SCL association addressing does not silently fall back.

## Runtime boundary

Step 2 does **not**:

- call `TpktClient`, `CotpClient`, or open TCP port 102;
- change `MmsClientSession.ConnectAsync`;
- select the new plan automatically;
- perform discovery, reads, writes, controls, report enable, or DataSet mutation.

The existing runtime continues to use `CotpConnectRequest.BuildDefault()` and the existing association profiles.

## Next step

Step 3 should add an opt-in SCL-assisted association entry point that consumes a validated plan, performs TCP/COTP/ACSE association, then validates only the live MMS domain inventory with `GetNameList(Domain, VMD)`. It must not repeat NamedVariable/GVAA/DataSet discovery when the trusted SCL model is being used.
