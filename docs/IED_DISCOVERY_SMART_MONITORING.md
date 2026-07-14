# IED Discovery Monitoring and Reporting

The IED Discovery Workbench presents live IEC 61850 values as engineering signals rather than as an unstructured MMS table.

## Online state

The **Online** command is an application state gate. Live Read, Enable RCB, Control, and monitor actions are available only when the IED session is connected and online-ready.

Online-ready means that the application has a usable association and model context. It does not prove that a station, switching procedure, or connected equipment is safe.

## Activity Monitor

- Pinned readable signals appear in the Activity Monitor.
- Report-covered signals are updated from received report frames.
- Polling is used only as fallback for pinned signals not covered by an active report DataSet.
- Quality, timestamp, reason, source, and age remain visible when available.
- Structured values are projected into readable child signals instead of being shown only as raw `Struct(...)` values.

## Report Control Block classification

RCBs are classified from refreshed live state as:

- static DataSet;
- available dynamic slot;
- enabled or reserved by another client/session;
- incomplete or unsupported.

An RCB that is enabled or reserved elsewhere is treated as occupied. The application does not silently overwrite its configuration.

## Persistent report workflow

1. Select an RCB or open it from the report list.
2. Refresh the live RCB state.
3. Build a static or dynamic report plan.
4. Review target, DataSet, trigger options, optional fields, ownership, and cleanup behavior.
5. Confirm the required writes.
6. Keep `RptEna=true` until **Stop RCB**, **Close IED**, session fault, or application shutdown.
7. Project received values into the Activity Monitor.
8. On stop, attempt to disable the RCB, release any reservation touched by the session, and remove a temporary dynamic DataSet created by the application.

## Dynamic slots

An empty RCB DataSet reference may represent an available dynamic slot rather than a broken static report. When allowed by the live server and approved by the user, the engine can build a temporary DataSet from pinned signals or selected priority ST/MX points.

Dynamic DataSet creation is an active write and remains behind a typed plan and confirmation.

## Current boundary

Persistent monitoring is implemented, but full BRCB recovery remains partial. `EntryID` resume, purge decisions, buffer overflow recovery, ownership variation, reconnect, and long-duration reliability require additional validation.
