# Full Stack Roadmap

ARIEC61850 is being built as a native C# IEC 61850 engineering stack. The target is not just packet encoding; the target is a practical lab suite that can discover IEDs, inspect SCL, publish/receive process-bus traffic, plan reports safely, simulate models, and export repeatable evidence.

## Capability matrix

| Capability | Current status | Next maturity step |
|---|---|---|
| BER / MMS presentation foundation | Implemented in core library | Expand negative/malformed PDU tests |
| TCP / TPKT / COTP / ACSE association | Implemented for MMS client use | Add association profile diagnostics in Discovery UI |
| MMS model discovery | Implemented in CLI and IED Discovery UI | Add live model tree drilldown and point search |
| DataSet directory | Implemented in core / CLI / IED Discovery UI | Add member-to-point binding quality indicators |
| RCB discovery and report planning | Implemented in core / CLI / IED Discovery UI | Add guided report setup wizard and saved profile |
| Report monitor / receive pump | Implemented in core / CLI | Add WPF runtime monitor workspace |
| BRCB recovery | Partial | Add resume/purge decision flow and EntryID evidence |
| GOOSE publish / parse / monitor | Implemented | Add live GoCB readback and quality bit details |
| SV publish / parse / injector UI | Implemented / in progress | Add live SV subscriber and timing evidence |
| PCAP read/write/replay | Implemented | Add one-click evidence bundle export |
| SCL parse and publisher profiles | Implemented | Add simulator import from SCL |
| IED simulator | Offline model/runtime foundation | Add read-only MMS server, then reports, then controlled writes |
| File/log/setting-group services | Future | Add read-only file/log browser first |
| Security profile | Future | Keep as separate maturity track after base stack stabilizes |

## Product workflow direction

### 1. IED Discovery

The discovery workspace should remain setup-oriented:

```text
Connect → Discover model → Inspect DataSets/RCBs → Validate readiness → Export report profile
```

This is where RCB and DataSet selection belongs. Runtime should not force the operator to keep reselecting RCBs unless the session profile changes.

### 2. Report Runtime

The runtime workspace should be evidence-oriented:

```text
Load profile → Enable guarded report → Trigger GI → Monitor reports → Export evidence → Cleanup
```

Visible runtime indicators should include active RCB, bound DataSet, member count, GI state, report count, sequence / entry movement, buffer overflow, and reason-for-inclusion.

### 3. IED Simulator

The simulator should mature in safe layers:

1. offline profile and deterministic value engine;
2. profile import/export;
3. read-only MMS model server;
4. DataSet directory and report control readback;
5. unbuffered reports;
6. buffered reports with EntryID and overflow evidence;
7. optional write/control handling for lab training.

Do not implement control/write behavior before read-only discovery and reporting behavior are stable.

## Recommended next phases

### Phase A — Discovery UI hardening

- Add model tree view: LD → LN → DO → DA.
- Add search box for object references.
- Add RCB readiness classification: safe, busy, missing DataSet, enabled, reservation conflict, unknown.
- Expand report profile export so it can be consumed directly by the Report Runtime workspace.

### Phase B — Report setup wizard

- Step 1: connect and discover.
- Step 2: choose DataSet.
- Step 3: choose compatible RCB candidate.
- Step 4: read back RCB state.
- Step 5: create guarded session profile.

### Phase C — Report runtime workspace

- Load saved session profile.
- Enable RCB with guarded writes.
- Trigger GI.
- Decode incoming reports.
- Show reason-for-inclusion per member.
- Export JSON/CSV evidence.

### Phase D — Simulator network core

- Build read-only MMS server skeleton.
- Expose domains, named variables, named variable lists, and access attributes.
- Add report control readback.
- Add unbuffered reports.

### Phase E — Process-bus receive maturity

- Add SV subscriber over the existing frame-source abstraction.
- Add timing/jitter summary for SV streams.
- Add GOOSE quality decoding and ConfRev mismatch evidence.
