# SCL-assisted MMS connect — Step 1

Status: **implemented in engine model/parser; unit-test coverage added; runtime association not yet integrated**.

## Purpose

This step introduces a typed, read-only SCL communication context for later SCL-assisted online connection. It does not alter current MMS discovery or association behavior.

The engineering observation motivating the work is vendor-neutral: when a trusted SCL/CID is available, a client can use local engineering information to resolve an IED access point and its association addressing before validating that model against the live MMS endpoint.

## Scope implemented

`SclMmsAssociationProfileReader` reads direct `Communication/SubNetwork/ConnectedAP/Address/P` parameters and projects known values into typed models:

- IED name and AccessPoint name;
- SubNetwork name/type;
- IP, subnet and gateway;
- OSI AP-title and AE qualifier;
- presentation, session and transport selectors;
- unknown direct `P` parameters retained as read-only engineering evidence.

Nested GSE/SMV `Address/P` values are deliberately excluded from the MMS association context.

Missing addresses remain explicit as unresolved access points. Invalid `OSI-AE-Qualifier` text is preserved and reported as a warning rather than silently discarded.

## Non-goals in Step 1

This step does **not**:

- open TCP port 102;
- change COTP TSAP behavior;
- generate ACSE AARQ bytes;
- perform `GetNameList`, GVAA, `Read`, DataSet discovery, write, control or report enable;
- treat SCL declarations as proof that a live endpoint matches the engineering model.

## Architecture boundary

```text
SCL XML
  -> SclMmsAssociationProfileReader
  -> typed SclMmsAccessPoint
       |- SclMmsEndpoint
       |- SclIsoAssociationAddress
       `- preserved direct parameters

(no network side effects)
```

The next lowest-risk step is to add a typed association-profile builder that consumes this context and produces transport/ACSE parameters without changing the existing default discovery profile. Live use should remain opt-in until byte-level and loopback validation is complete.
