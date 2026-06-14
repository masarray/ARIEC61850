# IED Discovery display policy

The IED Discovery Workbench uses a semantic IEC 61850 detail view instead of a protocol-style flat telemetry table.

## Detail grid

Data Object details are displayed as expandable child rows:

- `Name` carries the IEC 61850 hierarchy and expander state.
- `FC` shows the functional constraint for the row.
- `Type` shows the bound IEC 61850/MMS engineering type.
- `Value` shows the readable engineering value.

Quality, timestamp, and binding/debug status are not separate grid columns because they are part of the IEC 61850 data hierarchy. For example, `q` expands into named quality flags, and `t` expands into timestamp quality metadata. This avoids the misleading column noise that occurs when structured DA values are rendered like a flat SCADA protocol table.

## Explorer ordering

The Data Model explorer uses an engineering-first logical-node ordering. High-value SAS and SCADA logical nodes are placed near the top within each logical device, while all remaining nodes keep the discovered order as much as possible.

Priority groups:

1. `LLN0`, `LPHD`
2. Switchgear/control: `CSWI`, `XCBR`, `XSWI`, `CILO`, `PTRC`
3. Protection: `PTOC`, `PDIS`, `PDIF`, `PTOV`, `PTUV`, `PTOF`, `PTUF`, `PTEF`, `PTTR`, `PVOC`
4. Measurement/process-bus: `MMXU`, `MMXN`, `MMTR`, `MSQI`, `MHAI`, `MSTA`, `TCTR`, `TVTR`
5. Generic/application nodes such as `GGIO`, `GAPC`
6. Remaining nodes in discovered order

This is only a UI ordering policy. It does not modify the underlying live discovery document or exported SCL.
