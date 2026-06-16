# N5.44.1 — Sync-Aware SV Injection

This patch moves ARSVIN/SV Publisher one level above packet generation by connecting the new PTP TimeSync engine to the live SV publishing workflow.

## What changed

- Added `NpcapProcessBusDuplexTransport` so one selected NIC can transmit SV frames and monitor PTP traffic on the same adapter.
- Added live PTP passive monitoring during injection.
- Added Sync policy in Stream Config:
  - `AutoPtp`
  - `ForceUnsynchronized`
  - `ForceLocal`
  - `ForceGlobal`
- Replaced hard-coded `SampleSynchronization = 2` with policy-driven `smpSynch`.
- Added status bar visibility:
  - PTP state
  - domain
  - source clock identity
  - smpSynch value
- Added PTP domain and local fallback options.

## Safety behavior

`AutoPtp` does not silently claim global synchronization. The publisher uses:

- `smpSynch=2` only when PTP health is OK.
- `smpSynch=1` when PTP is visible but health is not fully OK and local fallback is enabled.
- `smpSynch=0` when no valid PTP is visible.

Operators can still force a value for isolated lab compatibility testing, but the selected policy is visible in the UI/status text.

## Scope

This is not a certified grandmaster implementation. It is a sync-aware SV publishing workflow that uses passive PTP visibility and health checks to prevent misleading `smpSynch` output.
