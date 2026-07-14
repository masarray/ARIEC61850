# IED Discovery Data Browser

The IED Discovery application is an engineering-oriented model browser. The left explorer stops at Data Object level, while the center detail grid renders Data Attributes, bound structure members, and read values.

## Expandable detail rows

Manual reads can return structured MMS values. The browser uses schema and live type information to render expandable rows:

```text
Pos
  stVal
  q
    Validity
    Overflow
    OutOfRange
    BadReference
    Failure
    OldData
    Inconsistent
    Inaccurate
    Source
    Test
    OperatorBlocked
  t
  Oper
  SBOw
  Cancel
```

This keeps large IED navigation manageable while preserving hierarchical IEC 61850 semantics.

## Read boundary

A manual read is enabled only for a resolved readable reference. The UI delegates FC resolution, type binding, and semantic expansion to the engine. It does not guess structure positions in application code.

## RCB detail and enable workflow

Selecting an RCB and clicking **Enable RCB** opens a report setup view showing:

- current live RCB state;
- static or dynamic classification;
- DataSet identity and member count;
- trigger options and optional fields;
- integrity period and optional GI;
- ownership or reservation evidence when available;
- planned writes and cleanup behavior.

After confirmation, the report monitor remains active until the user chooses **Stop RCB**, closes the IED, the session faults, or the application shuts down. Stop attempts to restore the state touched by the session, including disabling the RCB, releasing reservation, and deleting a temporary dynamic DataSet created by the application.

## Claim boundary

The browser and report workspace are engineering tools under active validation. They do not establish formal conformance, broad endpoint compatibility, operational-substation approval, or complete BRCB recovery support.
