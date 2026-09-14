# SCL-assisted trial convergence

Status: **trial-only convergence; unit/CI validation required; live IED interoperability not yet validated**.

## Baseline authority

This branch starts from ARSAS field-proven ARIEC61850 commit `11ab2304482600c19ba979f4fc9021ddb46b9af9`. Existing reporting, semantic projection, control and discovery behavior from that baseline are intentionally preserved.

The SCL-assisted transport/association/domain-validation/initial-read files are additive ports of reviewed Steps 1–4. This branch is not a replacement for `main` and its CI pull request must not be merged to `main`.

## Safe physical-trial contract

The first IED trial is read-only:

1. load and verify trusted SCL/CID;
2. derive exact called-side TSEL/SSEL/PSEL/AP-title/AE qualifier from the selected ConnectedAP;
3. open TCP/COTP/Session/Presentation/ACSE/MMS association;
4. validate only `GetNameList(Domain, VMD)` against expected SCL domains;
5. perform bounded initial FC-root Reads with at most 10 variable references per request and only one outstanding Read at a time;
6. preserve the SCL model when individual values are unavailable;
7. keep full discovery an explicit operator action.

The safe trial must not perform GVAA/NamedVariable enumeration, DataSet directory discovery, Write, control, RCB enable/GI, dynamic DataSet mutation, or automatic full-discovery fallback.

## Fail-closed addressing hardening

Conflicting duplicate critical association parameters (`OSI-TSEL`, `OSI-SSEL`, `OSI-PSEL`, `OSI-AP-Title`, `OSI-AE-Qualifier`) are treated as ambiguous. No value is selected by XML element order. AE qualifier is accepted only in the range `0..65535`.

## Trial evidence

A successful physical trial must retain:

- exact ARIEC convergence SHA;
- SCL source SHA-256 and selected IED/AP;
- TCP endpoint and association profile evidence;
- expected/observed/matched/missing/extra MMS domains;
- per-batch initial Read counts and failures;
- first-value latency;
- packet capture proving no hidden full discovery or writes occurred.
