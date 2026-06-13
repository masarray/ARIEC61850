# AGENTS

This file defines the working discipline for every human or AI agent modifying
`ARIEC61850`. The purpose is to keep the stack from becoming a demo that fails
halfway. Follow this file before writing code.

## 1. Mission

Build `ARIEC61850` as a clean-room IEC 61850 native stack and product foundation
for real engineering tools.

The stack must eventually support:

- MMS client.
- MMS server and IED simulator.
- live IED directory and smart FC resolution.
- read/write services.
- DataSet services.
- report services.
- safe control services.
- GOOSE publisher/subscriber.
- Sampled Values publisher/subscriber.
- SCL-driven station validation.
- CLI and WPF tester applications.

The reusable stack is the asset. Applications are clients of the stack.

## 2. Engineering Posture

This is protocol engineering, not demo coding.

Work principles:

- Build deterministic byte-accurate modules.
- Make every uncertainty visible.
- Prefer typed models over stringly typed application logic.
- Separate codecs, model building, runtime services, transport IO, and UI.
- Add tests before claiming a protocol behavior works.
- Preserve raw evidence for diagnostics.
- Keep public wording honest: tested, untested, unsupported, partial, or
  experimental.

Do not optimize for a pretty demo if it damages protocol architecture.

## 3. Clean-Room and License Rules

Allowed:

- Public documentation.
- Standard knowledge available to the team.
- Public product capability descriptions.
- Original implementation from protocol behavior.
- Black-box interoperability testing.
- PCAP comparison generated from permitted tools.
- Reading restrictive-license documentation/API documentation for capability planning.

Forbidden:

- Copying, translating, or mechanically porting restrictive-license implementation code.
- Reusing private SDK headers or proprietary generated source.
- Decompiling commercial tools.
- Copying proprietary UI layouts, icons, product text, or internal behavior.
- Adding `third-party IEC 61850 stacks` as a runtime dependency of this stack.
- Importing code from an incompatible license without explicit review.

If a source is restrictive-license or proprietary, treat it only as a feature checklist or
interop peer.

## 4. Repository Boundaries

Current projects:

```text
src/AR.Iec61850/                      reusable protocol stack
src/AR.Iec61850.Transports.Npcap/     raw Ethernet adapter
tests/AR.Iec61850.Tests/              unit and golden tests
apps/AR.Iec61850.Cli/                 CLI tester and smoke-test surface
apps/AR.Iec61850.SvPublisher/         WPF Sampled Values publisher workspace
```

Future projects may be split when stable:

```text
src/AR.Iec61850.Core/
src/AR.Iec61850.Mms/
src/AR.Iec61850.Model/
src/AR.Iec61850.Reporting/
src/AR.Iec61850.ProcessBus/
src/AR.Iec61850.Server/
src/AR.Iec61850.TestKit/
apps/AR.Iec61850.Workbench.Wpf/
tests/AR.Iec61850.InterOpTests/
```

Rules:

- Stack projects must not depend on WPF, WinForms, app view models, app
  settings, or product workflow state.
- Apps may depend on stack projects.
- Transport projects may depend on stack abstractions.
- Codecs must not depend on transports.
- TestKit may depend on stack internals only when explicitly justified.
- Do not duplicate protocol parsing in apps.
- Do not split projects before boundaries are stable.

## 5. Required Patch Workflow

Every protocol patch must follow this sequence.

### Step 1 - Understand the protocol job

Before coding, identify:

- IEC 61850 service or object involved.
- MMS service mapping involved.
- expected request/response PDU shape.
- state machine impact.
- safety impact: read-only, write, report enable, control, or publish.
- known vendor variation risk.

### Step 2 - Define typed models first

Create or update typed models before app logic.

Examples:

- `MmsVariableName`.
- `FunctionalConstraint`.
- `IedModelIndex`.
- `FcResolvedPoint`.
- `DataSetDirectory`.
- `ReportControlDirectory`.
- `ReportControlState`.
- `WritePlan`.

Do not let CLI string parsing become the source of truth.

### Step 3 - Add codec/request/response in core

Protocol encode/decode belongs in `src/`, not `apps/`.

Rules:

- Encode methods are deterministic.
- Decode methods preserve raw evidence.
- Unknown fields are preserved or reported, not silently discarded.
- Unsupported cases return explicit result/error objects.

### Step 4 - Add tests

Minimum tests for new protocol code:

- happy path encode,
- happy path decode,
- round-trip when applicable,
- malformed length or missing field,
- boundary value,
- unsupported tag or unknown value,
- ambiguity case if model resolution is involved.

Golden byte tests are required for low-level protocol PDUs when practical.

### Step 5 - Add CLI only after the stack API is stable

CLI commands are validation surfaces, not protocol engines.

The CLI may:

- parse command-line options,
- call stack services,
- print typed results and diagnostics.

The CLI must not:

- parse BER directly,
- build MMS PDUs directly,
- implement RCB state machine logic,
- hide stack diagnostics behind vague messages.

### Step 6 - Document limitations

Update docs when behavior changes.

Required updates when relevant:

- `ROADMAP.md` for milestone state or next patch order.
- `docs/VALIDATION.md` or `docs/validation/*` for hardware/interop evidence.
- `README.md` for user-visible commands.
- `docs/ARCHITECTURE.md` for architecture shifts.

## 6. MMS Architecture Rules

Build MMS strictly in layers:

```text
TCP
  -> TPKT
  -> COTP
  -> ISO Session
  -> ISO Presentation
  -> ACSE
  -> MMS
```

Rules:

- Do not bypass a lower layer to make a lab demo pass.
- Every layer exposes diagnostic trace events.
- Association state must be explicit.
- Release and abort must be modeled, not treated as random socket failure.
- Confirmed request/response must be matched by invoke ID.
- Timeouts and cancellation must be supported.
- Network reader logic must eventually be centralized in one receive pump per
  association.

## 7. Live IED Directory Rules

The stack must build a full IED directory after connect.

Directory must include:

- logical devices/domains,
- raw MMS named variables,
- FC-aware parsed variable names,
- logical nodes,
- data objects,
- data attributes,
- named variable lists / DataSets,
- DataSet members and order,
- report control blocks,
- relevant variable specifications when implemented,
- source and confidence for every resolved point.

Rules:

- Live MMS directory is the primary source for online workflows.
- SCL enriches and validates the live model; it does not replace live evidence.
- Heuristics are last resort and must be labeled as heuristics.
- Do not silently drop unknown vendor variables.
- Do not silently merge conflicting references.
- Stable ordering is required; live targets must not jump around in UI refresh.

## 8. Smart FC Resolver Rules

The resolver exists to make the stack easier to use than raw IEC 61850 APIs.

Resolver priority:

1. exact live MMS directory match,
2. normalized live user-reference match,
3. DataSet member directory match,
4. SCL match,
5. cached successful read,
6. bounded heuristic fallback,
7. controlled trial-read fallback.

Rules:

- Never require the user to enter FC for ordinary read/browse workflows when the
  stack can discover it.
- Never brute-force write/control FC values.
- Never hide ambiguity. Return candidate list with scores.
- A DO-level reference may legitimately map to several FCs. Report that clearly.
- Every resolved point must carry source and confidence.
- Cache successful resolutions for the session.
- Trial read is allowed only for read-only safe attributes, rate-limited, and as
  a final fallback.

Expected diagnostic language:

```text
Reference: OCR7SR12MEAS/MMXU1.PhV.phsA.cVal.mag.f
Resolved FC: MX
Source: LiveMms
Confidence: 100
MMS item: MMXU1$MX$PhV$phsA$cVal$mag$f
```

If ambiguous:

```text
Reference: OCR7SR12CTRL/XCBR1.Pos
Candidates:
  ST: XCBR1$ST$Pos$stVal/q/t
  CO: XCBR1$CO$Pos$Oper/SBO/Cancel
  CF: XCBR1$CF$Pos$ctlModel
Action: choose a leaf attribute or specify intent.
```

## 9. DataSet Rules

DataSet handling is foundational for GOOSE, SV, and MMS reports.

Rules:

- Preserve DataSet member order exactly.
- DataSet member mapping must include LD, LN, DO, DA path, FC, and source.
- DataSet directory from live IED must be supported even without SCL.
- SCL FCDA order must be cross-checked against live DataSet order when both
  exist.
- Report decoding must map values by DataSet member order.
- Dynamic DataSet create/delete must be explicit and guarded.
- Never create/delete a DataSet during discovery.

## 10. Reporting Rules

Reporting is a state machine.

Do not implement reporting as a single `RptEna=true` shortcut.

RCB handling must include:

- `RP` / URCB and `BR` / BRCB distinction.
- `DatSet`.
- `RptID`.
- `ConfRev`.
- `RptEna`.
- `Resv` / `ResvTms`.
- `Owner` if available.
- `OptFlds`.
- `TrgOps`.
- `BufTm`.
- `IntgPd`.
- `GI`.
- `PurgeBuf` for BRCB when needed.
- `EntryID`.
- `SqNum`.
- `TimeOfEntry`.
- `BufOvfl`.

RCB readiness classification must distinguish:

```text
Unknown
EmptyDynamicSlot
StaticBoundAvailable
EnabledByOtherClient
ReservedByOtherClient
AvailableForReservation
ReservedByMe
EnabledByMe
AccessDenied
ConfigurationRejected
Unsupported
```

Safety rules:

- If `RptEna=true` and the stack did not enable it, treat it as occupied.
- If reserved by another client, do not use it unless an explicit lab-mode force
  command is implemented and confirmed.
- Do not overwrite `DatSet`, `OptFlds`, `TrgOps`, or `RptID` while the RCB is
  enabled.
- Disable in reverse order and release reservation cleanly.
- BRCB recovery must respect `EntryID` and buffer state.
- Report monitor must show sequence and buffer evidence, not just values.

## 11. Async Receive Pump Rules

Reports arrive as unconfirmed/unsolicited MMS PDUs. A synchronous
send-then-read-only design is not enough.

Required architecture:

```text
MmsReceivePump
  -> strips transport/presentation/session layers
  -> decodes MMS PDU
  -> routes ConfirmedResponse by invoke ID
  -> routes ConfirmedError/Reject/Abort to pending operation/session diagnostics
  -> routes Unconfirmed InformationReport to report dispatcher
```

Rules:

- Only one reader loop should read from the network stream.
- Confirmed requests wait on invoke-specific completion objects.
- Report dispatch must not block the receive pump.
- A report arriving during a write/read request must not corrupt the pending
  confirmed operation.
- Receive pump errors must include raw evidence and session state.

## 12. Confirmed Write Rules

Writes are powerful and must be explicit.

Rules:

- No write during discovery.
- No trial write.
- No generic write for control service paths.
- Every write creates a `WritePlan` first.
- `WritePlan` shows target, resolved FC, MMS item, value type, encoded value,
  expected access risk, and rollback/disable plan when relevant.
- CLI/UI must show confirmation for live writes.
- Tests must include successful write encoding and write-error decoding.

## 13. Control Rules

Control is not generic write.

Rules:

- Discover `ctlModel` first.
- Respect direct operate, SBO, and enhanced security models.
- Include origin/check/test/interlock/synchrocheck handling where applicable.
- Provide command lifecycle diagnostics.
- Disable generic control workflows by default in public tooling.
- Require explicit lab-mode confirmation for live control operations.

## 14. GOOSE Rules

Publisher must control:

- APPID,
- destination MAC,
- source MAC,
- VLAN ID and priority,
- `goCbRef`,
- `datSet`,
- `goID`,
- `t`,
- `stNum`,
- `sqNum`,
- `test`,
- `confRev`,
- `ndsCom`,
- `numDatSetEntries`,
- typed `allData`.

Rules:

- `stNum` increments only on state change.
- `sqNum` resets on state change and increments on retransmit.
- retransmission schedule must be deterministic and testable.
- subscriber must supervise TimeAllowedToLive.
- SCL DataSet order is the semantic order when SCL exists.
- Without SCL, decode values but label them semantically anonymous.

## 15. Sampled Values Rules

Publisher must control:

- APPID,
- destination MAC,
- source MAC,
- VLAN ID and priority,
- `svID` / `smvID`,
- DataSet reference,
- `smpCnt`,
- `confRev`,
- `refrTm` when supplied,
- `smpSynch`,
- `smpRate`,
- `smpMod`,
- `nofASDU`,
- raw sample payload or typed payload mapping.

Rules:

- `smpCnt` wrap behavior must be explicit and tested.
- Do not assume one global channel order.
- DataSet entry order comes from SCL or live model evidence.
- Publishing at real SV rates requires pacing/performance tests.
- Normal Windows/Npcap timing must be described as lab/screening-level unless
  proven otherwise.

## 16. SCL Rules

SCL is the engineering model and validation source.

Parser must support:

- SCD, CID, ICD, IID, and XML SCL exports.
- IEC 61850-6 namespace variations.
- header metadata.
- IED, AccessPoint, LDevice, LN0, LN.
- DataSet and FCDA order.
- GSEControl.
- SampledValueControl.
- ReportControl.
- LogControl later.
- SettingControl later.
- Communication/GSE and Communication/SMV addresses.
- DataTypeTemplates with CDC, bType, enum type, type id resolution.

Rules:

- Preserve order.
- Keep parse warnings.
- Support multiple files in one context.
- Show conflicts instead of choosing silently.
- Never silently correct semantic mapping.
- Compare SCL against live model when both exist.

## 17. UI and Product Rules

Do not build UI before the stack workflow exists.

Product UX direction:

```text
Dense regulated engineering workbench for commissioning engineers, with calm
industrial cockpit UX, high evidence density, stable navigation, and direct
operator control.
```

Rules:

- First screen is the tool, not marketing.
- Use stable navigation and stable target ordering.
- Use center table/instrument for primary decisions.
- Use right inspector for selected evidence.
- Do not use nested cards.
- Do not use decorative gradients or ornamental blobs.
- Every warning must have an affected target.
- Do not hide critical evidence behind ellipsis without a detail view.
- Show statuses explicitly: PASS, WARNING, FAIL, UNKNOWN, MATCHED, WEAK,
  MISSING, UNEXPECTED, MISMATCH, CONFLICT.
- Raw hex belongs in advanced/evidence panels when typed decode exists.

## 18. Testing Commands

When .NET SDK is available, run:

```powershell
dotnet restore .\ARIEC61850.slnx
dotnet build .\ARIEC61850.slnx -c Release
dotnet test .\ARIEC61850.slnx -c Release --no-build
```

For protocol-specific patches, also run relevant CLI smoke tests when hardware
or sample files exist.

Examples:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-scl .\samples\scl\minimal-station.scd
dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\out\demo.pcap
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-pcap .\out\demo.pcap
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-discover 192.16.1.157 --port 102 --timeout-ms 20000 --max-report-probes 16
```

If a tool/runtime is unavailable, state that clearly in the patch report.

## 19. Status Reporting Per Patch

Every meaningful patch must report:

1. what changed,
2. why it is architecturally safer,
3. what is now more reusable,
4. what was validated,
5. what remains unproven,
6. tests/commands run,
7. next safest patch.

Never claim a feature is complete if only a happy-path demo was run.

## 20. Current Next Patch

The current next patch is **Live MMS Model Index**.

Implement in this order:

1. `FunctionalConstraint` enum.
2. `MmsVariableName` parser for `LN$FC$DO$DA$BDA`.
3. `Iec61850UserReference` normalizer for `LD/LN.DO.da.bda`.
4. `IedModelIndexBuilder` from existing `GetNameList` results.
5. `FcResolvedPoint` with source/confidence.
6. CLI `mms-model`.
7. Unit tests for parsing, normalization, ambiguity, and FC extraction.

After that:

1. `FcResolver`.
2. `ReadSmartAsync`.
3. CLI `mms-resolve`.
4. CLI `mms-read`.
5. DataSet member directory.
6. RCB readiness classification.
7. confirmed write.
8. report enable/GI.
9. async InformationReport receive.

## 21. Do Not Do

- Do not make a WPF report screen before report state machine exists.
- Do not make RCB enable a direct button without readiness classification.
- Do not hide FC resolution failure behind generic read failed messages.
- Do not keep app workflow dependent on the user typing ST/MX/CO manually.
- Do not run brute-force writes.
- Do not use report RCBs already enabled by another client.
- Do not assume RCB without DataSet is an error; classify it as possible dynamic
  slot until proven otherwise.
- Do not assume SCL is identical to live IED model.
- Do not assume all vendors expose owner/reservation in the same way.
- Do not move active publish/control behavior into passive analyzer products.
- Do not delete tests to make a build pass.
- Do not claim formal conformance before formal conformance evidence exists.
