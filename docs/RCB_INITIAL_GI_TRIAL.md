# Static RCB Initial-GI Field Trial Gate

This gate validates the interactive-client startup contract before PR #131 is merged.
It is intentionally evidence-first: use live MMS discovery as the authority for concrete RCB instance names and use SCL only as declarative context.

## Current code-gate status

PR #131 previously passed source/provenance verification, restore, build, and tests on the reporting engine changes. The branch now also contains the dedicated `AR.Iec61850.ReportTrial` qualification app; freeze and trial only an exact head whose .NET CI is green.

The remaining merge blocker after exact-head CI is field evidence from an authorized IED trial. Verify at least one BRCB and one URCB initial-GI session plus one fail-closed negative case.

`RptEnabled@max` preservation remains a P1 diagnostics improvement. It is intentionally outside this merge-critical change because live MMS discovery is the authority for concrete online RCB instances. Future preservation of that SCL metadata must not be used to synthesize runtime names such as `Buffer01` or `Buffer02`.

## Target startup contract

```text
associate
  -> discover live RCB/DataSet inventory
  -> reconcile SCL ReportControl family to concrete live RCB instance
  -> select a safe instance
  -> reserve when required (URCB only)
  -> RptEna=true
  -> persistent report routing active
  -> one-shot GI=true
  -> initial mapped DataSet report
  -> event-driven reports
```

`AR.Iec61850.ReportTrial` invokes the same SCL-assisted registered-monitor bootstrap APIs intended for the ARSAS application path. It does not use the legacy `mms-report-monitor` guarded-session path.

## Safety prerequisites

- Use a laboratory IED or an authorized test window.
- Confirm no other client is using the RCB family selected for the test.
- Start read-only. The trial app performs discovery/family reconciliation/planning without writes unless `--yes` is supplied.
- Do not substitute a concrete `Buffer01`/`Unbuffer01` guess for the SCL family name. The app must resolve the concrete instance from live MMS evidence.
- Preserve each generated JSON evidence file for merge review.

## 1. Inspect both engineering files

```powershell
dotnet run --project apps/AR.Iec61850.Cli -- inspect-scl "<ED1_FILE.icd>"
dotnet run --project apps/AR.Iec61850.Cli -- inspect-scl "<ED2_FILE.iid>"
```

For the qualification shape used to design this gate, the declarative controls are family/base identities such as:

```text
.../LLN0$BR$Buffer
.../LLN0$RP$Unbuffer
```

Do not convert these to `Buffer01` / `Unbuffer01` from SCL alone.

## 2. Read-only dry run through the exact new path

Run each family first without `--yes`:

```powershell
dotnet run --project apps/AR.Iec61850.ReportTrial -- "<ED2_FILE.iid>" <IED_IP> --report Buffer --kind BRCB --port 102 --timeout-ms 120000 --max-report-probes 286 --initial-timeout-sec 10 --duration-sec 30 --evidence .artifacts/trial/brcb-dry-run.json

dotnet run --project apps/AR.Iec61850.ReportTrial -- "<ED2_FILE.iid>" <IED_IP> --report Unbuffer --kind URCB --port 102 --timeout-ms 120000 --max-report-probes 286 --initial-timeout-sec 10 --duration-sec 30 --evidence .artifacts/trial/urcb-dry-run.json
```

Acceptance:

- one SCL ReportControl family is selected;
- the family resolves only to concrete live RCB instances in the same domain/LN/report FC;
- the plan selects a safe concrete instance with a mapped DataSet;
- no report-control write occurs in dry-run mode;
- an ambiguous/missing family fails closed.

Repeat with the Edition 1 file to verify family reconciliation even if GI is not advertised there.

## 3. Optional direct read-only probe

After the dry run tells you which concrete RCB was selected, you may inspect it with the existing read-only CLI:

```powershell
dotnet run --project apps/AR.Iec61850.Cli -- mms-rcb-probe <IED_IP> "<CONCRETE_BRCB_REFERENCE>" --port 102 --timeout-ms 120000

dotnet run --project apps/AR.Iec61850.Cli -- mms-rcb-probe <IED_IP> "<CONCRETE_URCB_REFERENCE>" --port 102 --timeout-ms 120000
```

## 4. BRCB initial-GI trial

For the Edition 2 file/capability set where GI is live-exposed:

```powershell
dotnet run --project apps/AR.Iec61850.ReportTrial -- "<ED2_FILE.iid>" <IED_IP> --report Buffer --kind BRCB --port 102 --timeout-ms 120000 --max-report-probes 286 --initial-timeout-sec 10 --duration-sec 30 --evidence .artifacts/trial/brcb-initial-gi.json --yes
```

Required evidence:

1. SCL family `Buffer` resolves to a concrete live BRCB such as `Buffer01` only because that instance exists live;
2. no URCB-style `Resv=true` pre-reservation is performed;
3. `RptEna=true` succeeds;
4. persistent routing is already registered when the one-shot `GI=true` is sent;
5. a mapped InformationReport containing initial DataSet values is received in the bootstrap window;
6. the steady-state slice performs zero additional GI writes and zero polling reads;
7. subsequent changes can arrive through normal reporting;
8. cleanup disables `RptEna` when this client enabled it.

The trial command returns exit code `0` only when initial values arrive through the new GI bootstrap path and cleanup succeeds.

## 5. URCB initial-GI trial

```powershell
dotnet run --project apps/AR.Iec61850.ReportTrial -- "<ED2_FILE.iid>" <IED_IP> --report Unbuffer --kind URCB --port 102 --timeout-ms 120000 --max-report-probes 286 --initial-timeout-sec 10 --duration-sec 30 --evidence .artifacts/trial/urcb-initial-gi.json --yes
```

Required evidence:

1. SCL family `Unbuffer` resolves to a concrete live URCB;
2. `Resv=true` occurs before `RptEna=true` when the live RCB exposes `Resv`;
3. `RptEna=true` succeeds;
4. persistent routing is active before the one-shot GI request;
5. a mapped InformationReport containing initial DataSet values is received;
6. the steady-state slice performs no periodic GI and no polling;
7. event-driven reporting remains active;
8. cleanup disables `RptEna` and releases a reservation touched by this client.

## 6. Same-family contention trial

When the laboratory setup permits it, make the first indexed instance unavailable and leave the next instance free, then run the dry-run or live trial again using the same SCL family name.

Acceptance:

- an indexed SCL family such as `Buffer` may select a concrete live sibling such as `Buffer02` only if that sibling is actually discovered live;
- the planner must not escape to an unrelated RCB merely because it is free;
- no `01`/`02` runtime instance may be synthesized from `RptEnabled@max` or from a naming guess.

## 7. Negative/fail-closed trials

Capture at least one negative case before merge:

- live RCB directory does not expose `GI` -> no blind GI write;
- SCL family has no matching live concrete instance -> no report-control write;
- case-mismatched domain/LN/RCB identity -> no automatic match;
- intended family has no safe free member -> no write;
- DataSet member mapping is absent/ambiguous -> bootstrap planning is blocked.

Edition 1 is useful for the no-GI-capability case when the live RCB inventory actually omits `GI`: the monitor may still start, but the engine must not fabricate GI support.

## 8. Trial evidence package

Keep the JSON evidence from every dry-run/live run. For merge review verify at minimum:

- exact branch/head SHA used for the trial;
- IED model/firmware identifier if available;
- SCL edition/file used;
- SCL family reference and matched live references;
- selected concrete RCB reference;
- DataSet reference/member mapping;
- live RCB attributes including GI capability;
- ordered start/bootstrap write steps;
- first mapped InformationReport after GI and value count;
- steady-state `GiWriteCount == 0` and `PollReadCount == 0`;
- Stop/Close cleanup result;
- one negative/fail-closed case and its reason.

Do not commit raw proprietary PCAP/SCL captures to the public repository. Preserve them locally and derive project-owned synthetic vectors when a permanent regression fixture is required.

## Merge gate

Code side is merge-candidate quality only when:

- .NET CI is green on the exact frozen PR head, including the `AR.Iec61850.ReportTrial` app;
- synthetic Ed.1/Ed.2 family-resolution regressions are green;
- BRCB and URCB family-planning regressions are green;
- live GI capability discovery regressions are green;
- no runtime RCB instance is synthesized from SCL metadata;
- the field-trial executable builds from the same solution as the library/tests.

Field side is complete only when:

- one authorized BRCB initial-GI run passes;
- one authorized URCB initial-GI run passes;
- no periodic-GI or polling dependency is needed to populate/maintain the reporting path;
- cleanup is verified after Stop/Close;
- at least one fail-closed negative case is observed or reproduced safely.

Keep PR #131 Draft while field evidence is pending. `RptEnabled@max` preservation is useful future diagnostics metadata, but it is deliberately not a merge blocker and must never become authority for synthesizing runtime RCB names.
