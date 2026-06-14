# IED Discovery session workflow fixes

This milestone tightens the IED Discovery Workbench around four field-facing workflows:

- report selection now uses Smart RCB fallback instead of failing when the selected RCB is locked or reserved;
- Close IED clears the explorer, details, monitor pins, and discovery state;
- Save SCL exports through the engine exporter with an IID-oriented default extension;
- Open SCL projects an offline SCL model into the same explorer/detail shape used by live discovery.

## Reporting behavior

The workbench first tries the selected RCB. If the selected RCB is busy, enabled, reserved, missing a usable DataSet directory, or not an empty dynamic slot, the app asks the engine planner to search for the safest available static/dynamic candidate instead of surfacing a raw strict-selection error immediately.

This keeps the UI aligned with field workflow: locked RCBs remain visible for diagnostics, but the app can still use a safe alternative when one exists.

## SCL behavior

Opening an SCL file now builds an offline model projection from IED, LDevice, LN, DataTypeTemplates, DataSet, ReportControl, GSEControl, and SampledValueControl sections. The explorer therefore behaves like the live-discovered model where possible, while live read/report/control actions remain disabled until an MMS session is online.
