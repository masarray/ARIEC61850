# AGENTS

This file defines the working discipline for every human or automated agent modifying ARIEC61850.

## 1. Mission

Build ARIEC61850 as an independently developed IEC 61850 stack and product foundation for real engineering tools.

The reusable stack is the primary asset. Applications consume stack capabilities; they do not define protocol behavior.

## 2. Engineering posture

This is protocol engineering, not demo coding.

- Build deterministic, byte-accurate modules.
- Make uncertainty and unsupported behavior visible.
- Prefer typed models over string-driven application logic.
- Separate codecs, models, runtime services, transports, and UI.
- Add tests before claiming behavior works.
- Preserve only project-owned or lawfully redistributable evidence.
- Use claim labels such as `implemented`, `unit tested`, `loopback verified`, `laboratory exercised`, `partial`, and `not validated`.

Do not optimize for a visually attractive demo at the expense of protocol architecture, provenance, or operational guardrails.

## 3. Independent-development and provenance rules

Permitted implementation sources:

- lawfully obtained public standards and published protocol specifications;
- public errata and vendor-neutral technical guidance;
- independently written engineering analysis;
- project-owned encoders, fixtures, synthetic SCL, tests, diagrams, and documentation;
- lawful black-box interoperability observations reduced to minimum protocol facts and independently reconstructed in project code.

External software may be used only as a lawfully licensed black-box interoperability endpoint. Its source, API composition, examples, tests, documentation wording, UI, resources, internal files, and implementation structure must not be used as design material.

Forbidden:

- copying, translating, mechanically porting, or adapting external implementation code;
- using code-generation tools to translate another implementation;
- decompilation, disassembly, symbol extraction, reflection, memory inspection, resource extraction, or technical-restriction circumvention;
- reproducing a distinctive external API merely to claim compatibility;
- importing private SDK headers, generated wrappers, binaries, manuals, screenshots, icons, logos, reports, or extracted resources;
- committing raw external-client captures as permanent fixtures;
- using confidential customer, employer, station, or project material;
- presenting another implementation or product as the authority for ARIEC61850 behavior.

When externally observed behavior is relevant:

1. record only the vendor-neutral protocol fact;
2. verify it against a public protocol grammar where practicable;
3. reconstruct it using project-owned codecs;
4. create a synthetic minimal fixture;
5. document lawful provenance and review.

## 4. Repository boundaries

```text
src/      reusable protocol, simulation, and transport libraries
apps/     CLI and thin Windows engineering workspaces
tests/    deterministic unit, integration, and regression tests
samples/  synthetic, project-owned examples
docs/     user, architecture, validation, and assurance documentation
```

Rules:

- Stack projects must not depend on UI frameworks or app workflow state.
- Apps may depend on stack projects.
- Transports depend on stack abstractions; codecs do not depend on transports.
- Protocol parsing and state machines belong in `src/`, not `apps/`.
- Generated evidence and build output stay outside tracked source.
- New fixtures require documented provenance and redistribution rights.

## 5. Required patch workflow

### Step 1 — Define the protocol job

Identify:

- IEC 61850 service or object;
- MMS mapping and expected PDU shape;
- state-machine impact;
- read, write, report, control, or publish risk;
- known variation or ambiguity;
- evidence needed to support the claim.

### Step 2 — Define typed models

Create or update typed contracts before application logic. Unknown and ambiguous states must remain explicit.

### Step 3 — Implement in engine layers

Encode and decode logic belongs in reusable libraries. Unknown fields must be preserved or reported rather than silently discarded.

### Step 4 — Add deterministic tests

Minimum coverage where applicable:

- encode and decode happy paths;
- round trip;
- malformed length or missing field;
- boundary value;
- unsupported or unknown value;
- ambiguity and state-transition cases;
- cancellation, timeout, and cleanup;
- negative service result.

Golden byte tests are required for low-level protocol PDUs when practical. Golden material must be project-generated or independently reconstructed from a public grammar.

### Step 5 — Add CLI or UI only after the engine API is stable

CLI and UI are validation and product surfaces. They must not become alternative protocol engines.

### Step 6 — Update documentation

Update the appropriate files:

- `README.md` for user-visible scope;
- `docs/ENGINE_MATURITY_MATRIX.md` for current evidence;
- `ROADMAP.md` for future work only;
- `CHANGELOG.md` for completed changes;
- `docs/VALIDATION.md` for repeatable procedures;
- `SECURITY.md` for security or active-network risk.

## 6. MMS architecture rules

Maintain explicit layers:

```text
TCP → TPKT → COTP → ISO Session → ISO Presentation → ACSE → MMS
```

- Do not bypass layers to make a demo pass.
- Association state, release, abort, timeout, and cancellation are explicit.
- Confirmed responses are matched by invoke ID.
- One receive pump owns network reads per association.
- Reports and asynchronous control evidence must not corrupt confirmed operations.

## 7. Model and resolver rules

- Live MMS directory is the primary source for online workflows.
- SCL enriches and validates; it does not replace live evidence.
- Preserve DataSet member order.
- Unknown variables remain visible.
- Heuristics are bounded, labeled, and never used for blind writes.
- Every resolution result carries source and confidence.

## 8. Reporting rules

Reporting is a state machine, not a single `RptEna=true` write.

- Read and classify current RCB state before writes.
- Treat an RCB enabled or reserved by another client as occupied.
- Do not overwrite configuration fields while enabled.
- Validate DataSet identity and member order.
- Cleanup in reverse order and preserve evidence.
- BRCB recovery must account for entry and overflow state.

## 9. Write and control rules

- No write during discovery.
- No trial write.
- No generic write to control-service members.
- Build and display a typed plan before active writes.
- Require explicit user confirmation and approved test conditions.
- Discover `ctlModel` and exact live type information before control.
- Keep acceptance, command termination, application error, and process feedback as separate outcomes.
- Do not describe protocol guardrails as proof that equipment or a switching procedure is safe.

## 10. Process-bus rules

GOOSE and Sampled Values publishing must be deterministic, bounded, and explicitly armed.

- Require explicit adapter selection.
- Provide dry-run or in-memory paths.
- Preserve sequence, timing, DataSet order, and configuration evidence.
- Describe ordinary Windows timing as laboratory or screening evidence unless stronger timing validation exists.
- Never publish on an operational network without an approved plan, authority, and isolation boundary.

## 11. UI and product rules

- UI follows engine workflows; it does not invent protocol semantics.
- Use stable navigation and stable target ordering.
- Put typed evidence in primary views and raw hex in advanced views.
- Use explicit labels: PASS, WARNING, FAIL, UNKNOWN, MATCHED, PARTIAL, MISSING, UNEXPECTED, MISMATCH, CONFLICT.
- Avoid wording that implies certification, regulatory approval, autonomous operation, universal interoperability, or operational safety.
- Independently design layout, icons, artwork, wording, and interaction details.

## 12. Validation commands

```powershell
dotnet restore .\ARIEC61850.sln
dotnet build .\ARIEC61850.sln -c Release
dotnet test .\ARIEC61850.sln -c Release --no-build
.\scripts\verify-source-clean.cmd
```

Use documentation-only addresses such as `192.0.2.10`, `198.51.100.10`, or `203.0.113.10` in public examples.

## 13. Patch report

Every meaningful patch reports:

1. what changed;
2. why the architecture or provenance is stronger;
3. what was validated;
4. what remains unproven;
5. commands run;
6. the next lowest-risk step.

Never claim completion from a happy-path demonstration alone.
