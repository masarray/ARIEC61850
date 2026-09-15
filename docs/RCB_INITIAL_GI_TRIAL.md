# Static RCB Initial-GI Field Trial Gate

This gate validates the interactive-client startup contract before PR #131 is merged.
It is intentionally evidence-first: use live MMS discovery as the authority for concrete RCB instance names and use SCL only as declarative context.

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

`GI` is a one-shot initial-value bootstrap. Do not enable periodic GI during this qualification.

## Safety prerequisites

- Use a laboratory IED or an authorized test window.
- Confirm no other client is using the RCB selected for the test.
- Start with live discovery/readiness. Do not assume an SCL base name is a concrete runtime instance.
- Keep `--strict-rcb` and `--allow-urcb-fallback false` for the qualification runs so the evidence belongs to the intended RCB.
- Preserve the generated evidence directory for review.

## 1. Inspect both engineering files

```powershell
dotnet run --project apps/AR.Iec61850.Cli -- inspect-scl "<ED1_FILE.icd>"
dotnet run --project apps/AR.Iec61850.Cli -- inspect-scl "<ED2_FILE.iid>"
```

For the Siemens-derived qualification shape used to design this gate, the declarative controls are family/base identities such as:

```text
.../LLN0$BR$Buffer
.../LLN0$RP$Unbuffer
```

Do not convert these to `Buffer01` / `Unbuffer01` from SCL alone.

## 2. Discover and classify the live RCB pool

```powershell
dotnet run --project apps/AR.Iec61850.Cli -- mms-report-plan <IED_IP> --port 102 --timeout-ms 120000 --max-report-probes 286 --raw-limit 0
```

Acceptance:

- concrete live RCB instances are visible, for example `...BR.Buffer01` / `...RP.Unbuffer01` when that is what the IED actually exposes;
- selected test RCB has a valid DataSet and is not already enabled/reserved by another client;
- `GI` must be present in the live RCB attribute inventory before the initial-GI bootstrap is treated as supported.

If identity or ownership is ambiguous, stop. No report-control write is an acceptable fail-closed result.

## 3. Probe the intended BRCB and URCB

Replace the example references below with the concrete names discovered in step 2.

```powershell
dotnet run --project apps/AR.Iec61850.Cli -- mms-rcb-probe <IED_IP> "AA1E1F06R4Application/LLN0.BR.Buffer01" --port 102 --timeout-ms 120000

dotnet run --project apps/AR.Iec61850.Cli -- mms-rcb-probe <IED_IP> "AA1E1F06R4Application/LLN0.RP.Unbuffer01" --port 102 --timeout-ms 120000
```

Acceptance:

- DataSet reference resolves and its member directory can be mapped;
- `RptEna` state is known;
- URCB reservation state is known when exposed;
- the test RCB is safe to claim;
- GI capability is live-evidenced rather than inferred only from SCL.

## 4. BRCB initial-GI trial

```powershell
dotnet run --project apps/AR.Iec61850.Cli -- mms-report-monitor <IED_IP> --port 102 --timeout-ms 120000 --rcb "AA1E1F06R4Application/LLN0.BR.Buffer01" --strict-rcb --allow-urcb-fallback false --duration-sec 30 --gi true --gi-interval-sec 0 --soak-snapshot-sec 0 --evidence .artifacts/trial/brcb-initial-gi --yes
```

Required evidence:

1. selected RCB is the requested concrete BRCB;
2. no URCB-style `Resv=true` pre-reservation is performed;
3. `RptEna=true` succeeds;
4. one initial `GI=true` is issued;
5. a mapped InformationReport containing initial DataSet values is received;
6. no periodic GI is issued (`--gi-interval-sec 0`);
7. subsequent value changes arrive through normal reporting;
8. stop/disconnect disables `RptEna` when this client enabled it.

## 5. URCB initial-GI trial

```powershell
dotnet run --project apps/AR.Iec61850.Cli -- mms-report-monitor <IED_IP> --port 102 --timeout-ms 120000 --rcb "AA1E1F06R4Application/LLN0.RP.Unbuffer01" --strict-rcb --allow-urcb-fallback false --duration-sec 30 --gi true --gi-interval-sec 0 --soak-snapshot-sec 0 --evidence .artifacts/trial/urcb-initial-gi --yes
```

Required evidence:

1. selected RCB is the requested concrete URCB;
2. `Resv=true` occurs before `RptEna=true` when the live RCB exposes `Resv`;
3. `RptEna=true` succeeds;
4. one initial `GI=true` is issued;
5. a mapped InformationReport containing initial DataSet values is received;
6. no periodic GI is issued;
7. normal event-driven reporting continues;
8. stop/disconnect disables `RptEna` and releases a reservation touched by this client.

## 6. Same-family contention trial

When the laboratory setup permits it, make the first indexed instance unavailable and leave the next instance free, then execute SCL-assisted planning.

Acceptance:

- an indexed SCL family such as `Buffer` may select a concrete live sibling such as `Buffer02` only if that sibling is actually discovered live;
- the planner must not escape to an unrelated RCB merely because it is free;
- no `01`/`02` runtime instance may be synthesized from `RptEnabled@max` or from a naming guess.

## 7. Negative/fail-closed trials

At least one negative case should be captured before merge:

- live RCB directory does not expose `GI` -> no blind GI write;
- SCL family has no matching live concrete instance -> no report-control write;
- case-mismatched domain/LN/RCB identity -> no automatic match;
- intended RCB is already enabled/reserved by another client -> leave it untouched or select another proven-free member of the same family;
- DataSet member mapping is absent/ambiguous -> do not project report values against an arbitrary DataSet.

## Merge gate

The code side of PR #131 is merge-candidate quality when:

- .NET CI is green on the exact PR head;
- synthetic Ed.1/Ed.2 family-resolution regressions are green;
- BRCB and URCB family-planning regressions are green;
- live GI capability discovery regressions are green;
- no runtime RCB instance is synthesized from SCL metadata.

The field side is complete only when:

- at least one authorized BRCB and one URCB initial-GI run satisfy the evidence above;
- no periodic-GI dependency is needed to populate the initial client values;
- cleanup is verified after Stop/Close;
- at least one fail-closed negative case is observed or reproduced safely.

Keep the PR Draft while field evidence is still pending. `RptEnabled@max` preservation is useful future diagnostics metadata, but it is deliberately not a merge blocker and must never become authority for synthesizing runtime RCB names.
