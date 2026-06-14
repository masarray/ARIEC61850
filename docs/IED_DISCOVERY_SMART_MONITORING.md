# IED Discovery smart monitoring and reporting milestone

The IED Discovery Workbench now treats live values as an engineering view instead of a raw MMS table.

## Behaviour

- The Online command is a state gate. Live Read, Enable RCB, Control, and polling monitor actions are only available when the IED session is connected and online-ready.
- The Activity Monitor is live for pinned signals. Pinned readable DA rows are polled while the session is online.
- Report frames from guarded RCB enable sessions are pushed into the Activity Monitor as report-driven values.
- Report Control Blocks are classified as static DataSet, dynamic slot, occupied, or incomplete.
- Occupied RCBs show a lock marker in the explorer when RptEna/Resv/ResvTms indicates that another client or existing session is using the block.
- Dynamic slots are no longer treated as broken static reports; the engine can build a temporary dynamic DataSet plan from pinned signals or safe priority ST/MX points.

## Design policy

IEC 61850 values are hierarchical. Quality and timestamp remain part of the child-row semantic model, not side columns. The monitor table is intentionally compact and only shows live value, quality summary, age, and source.

## Scope boundary

This is still an engineering alpha. Guarded report enable is deliberately short-lived and cleans up RptEna/DataSet state where applicable. Long-running report subscriptions and persistent project files are scheduled for later product milestones.
