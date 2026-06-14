# IED Discovery Workbench Shell

The IED Discovery Workbench is the first usable WPF shell for live IEC 61850 model exploration. It is intentionally dense, command-oriented, and read-safe by default.

## Scope

The shell focuses on the engineering workflow that is needed before report, control, or monitor features can be trusted:

1. open an SCL file for offline inspection;
2. discover a live IED from IP/port parameters;
3. keep the UI responsive while discovery is running;
4. render a compact IED explorer that stops at Data Object level;
5. show Data Attribute details in the center panel;
6. pin selected signals to the Activity Monitor panel;
7. keep protocol progress and errors in a capped status-history buffer;
8. export a discovered live model to JSON or generated SCL.

## Layout

```text
Top command bar
  Open SCL | Save SCL | Discover | Close IED | Online | Read | Read all | Enable RCB | Control | Pin | Export

Left explorer
  IED
    GOOSE
    Reports
    Setting Groups
    Files
    DataSets
    Data Model
      LD
        LN
          DO

Middle detail panel
  Data Attribute rows, DataSet members, RCB attributes, or selected object metadata

Right monitor panel
  Pinned signals for future polling/report-driven monitoring

Bottom status history
  Capped ring buffer of information, warnings, errors, and protocol progress
```

## Rendering rules

The UI must not render every low-level MMS response directly. The workbench uses stage-level progress and batched model rendering so large IEDs do not freeze the window while the model is being discovered.

Rules:

- discovery runs off the UI thread;
- model rendering is batched after a snapshot is built;
- status history is capped at 500 rows;
- tree nodes stop at DO level;
- DA details are rendered only for the selected DO;
- Control and Enable RCB buttons are context-aware and disabled when the selected object is not safe or relevant.

## Current limitations

This milestone is a shell and workflow milestone. It does not yet implement full live report monitoring, live operate, or high-frequency polling. Those features are intentionally staged after the browser shell is stable.

