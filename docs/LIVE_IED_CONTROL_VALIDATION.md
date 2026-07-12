# Live IED Control Validation

## Validation record

**Date:** 12 July 2026

**Environment:** Windows, .NET 8 Release build, isolated laboratory IED network

**Client:** ARIEC61850 IED Discovery Smart Control Tester

The native control request path was exercised against a real laboratory IED. A command selected from the Smart Control Tester reached the IED through the ARIEC61850 control stack after live discovery of the control object.

This record intentionally avoids publishing customer, site, IP-address, serial-number, vendor, and firmware details. Those details should remain in a private test report when required for project acceptance.

## Confirmed in this validation

- Release solution build completed on the Windows test workstation.
- IED association and live model discovery completed.
- A controllable Data Object based on `CSWI.Pos` was opened through the native control service.
- The tester presented operator intent as OPEN/CLOSE rather than exposing raw `Oper`, `SBO`, or `SBOw` writes.
- The command request traversed the ARIEC61850 live control path and reached the IED.
- The UI and engine retained protocol evidence for diagnosis.

## Not established by this validation

This single live test does not establish:

- formal IEC 61850 conformance;
- interoperability with every IED vendor, model, or firmware release;
- successful coverage of all four control models;
- every negative CommandTermination and AddCause path;
- long-duration repeated-operation reliability;
- operational-substation safety approval;
- process movement unless independently verified by status, indications, and the approved test procedure.

## Recommended evidence for each IED family

Record the following before describing an IED family as validated:

1. IED vendor, model, firmware, SCL revision, and test date in a private report.
2. Control Data Object, CDC, discovered `ctlModel`, and live `Oper`/`SBOw`/`Cancel` type signatures.
3. Initial status value and quality.
4. Origin category and Orig ID accepted by the IED.
5. Interlock, synchrocheck, and Test settings.
6. Request, immediate MMS response, CommandTermination, LastApplError, ControlError, and AddCause evidence.
7. Final status value and independent process indication.
8. SBO timeout and explicit Cancel behavior where applicable.
9. Association-loss and competing-client behavior.
10. Repeated OPEN/CLOSE cycles within the approved laboratory procedure.

## Public claim wording

Recommended public wording:

> The ARIEC61850 Smart Control Tester has completed an end-to-end command path with a laboratory IED. Multi-vendor validation and formal conformance testing remain ongoing.

Avoid wording that implies certification, universal interoperability, or permission for unrestricted field operation.
