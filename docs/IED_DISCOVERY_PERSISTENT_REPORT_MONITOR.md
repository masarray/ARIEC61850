# IED Discovery Persistent Report Monitor

N5.42 changes the IED Discovery Workbench from a short guarded report proof into an interactive report monitor.

## Why this exists

A desktop engineering tool must keep the selected report control block enabled until the user explicitly stops it. The previous guarded proof enabled `RptEna=true`, listened for a short evidence window, and then cleaned up by writing `RptEna=false`. That behavior is safe for CLI proof commands, but it is not the right behavior for an interactive monitor.

## Workflow

1. Select an RCB from the explorer or double-click an RCB in the report list.
2. Click **Enable RCB**.
3. The workbench probes the live RCB state again.
4. The engine builds a static or dynamic report plan.
5. The engine writes the required attributes and enables `RptEna=true`.
6. The monitor remains active until **Stop RCB**, **Close IED**, or app shutdown.
7. Reports are mapped back to DataSet members and pushed into the Activity Monitor.
8. Stop writes `RptEna=false`, releases reservation if touched, and removes temporary dynamic DataSet when the app created it.

## UX rules

- Report list rows are clickable. Double-click opens the RCB detail page without hunting for the same RCB in the tree.
- Running RCB state is refreshed immediately after enable and stop.
- The Activity Monitor is event-driven when reports are received and may use polling fallback for pinned signals.
- Static RCB uses the DataSet already assigned to the RCB.
- Dynamic RCB uses a temporary DataSet from pinned signals or priority ST/MX points.

## Safety boundary

This is still an alpha monitor. It does not yet implement full BRCB recovery strategy with EntryID resume, PurgeBuf decisioning, and BufOvfl recovery. Those remain later milestones.
