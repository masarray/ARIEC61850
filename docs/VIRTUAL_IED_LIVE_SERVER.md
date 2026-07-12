# Virtual IED Live MMS Server

This milestone turns the IED simulator from an offline/loopback evidence harness
into a virtual IED that real IEC 61850 clients can connect to over TCP, matching
the "Open SCL → Run" workflow of commercial simulators.

## What runs

`IedSimulatorMmsServer` and `MmsVirtualIedServer` are persistent, multi-client,
read-only MMS servers. For each accepted connection they:

1. completes the COTP handshake (Connection Request → Connection Confirm);
2. parses the client presentation context list, accepts the ACSE AARQ, and replies
   with a CPA/AARE + MMS InitiateResponse using the client's session parameters;
3. stores the negotiated MMS presentation context id for the connection and uses it
   on every subsequent Confirmed-Response;
4. serves a confirmed-request loop, dispatching each request through
   `MmsConfirmedRequestBerDispatcher` against the read-only server session:
   - `GetNameList` (domain / named-variable / named-variable-list browse),
   - `Read` (point values),
   - `GetNamedVariableListAttributes` (DataSet directory);
5. rejects every `Write` through the session's read-only guard.

Connections are isolated: a client that disconnects or sends a malformed frame
ends only its own session. The server raises `ConnectionAccepted`,
`ConnectionClosed`, `RequestDispatched`, and `ServerError` events so the CLI and
WPF app can show live activity.

## How it is built (reuse, not reinvention)

The wire-level work was already proven by the loopback BER profile. The server
composes the existing, tested codecs:

- `TpktFrameCodec`, `CotpFrameCodec` (OSI transport),
- `AcseMmsAssociateResponse`, `AcseAssociationPayloadInspector` (association),
- `MmsConfirmedRequestBerDispatcher` (native MMS BER request decode → model
  dispatch → response encode),
- `MmsReadOnlyServerModelBuilder` / `MmsReadOnlyServerSession` (the model + guard),
- `IedSimulatorProfileBuilder.FromScl` (N5.45) for the model itself.

The discovery model includes the logical-node and functional-constraint hierarchy
(`LN`, `LN$FC`) as well as full leaf names (`LN$FC$DO$DA...`). This preserves the
flat names needed by the native resolver while allowing engineering browsers to
construct a navigable IEC 61850 tree.

## Using it

CLI — run a live IED from an SCL file and discover it from another shell:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- simulate-ied --scl .\samples\scl\minimal-station.scd --port 102 --steps 5
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-discover 127.0.0.1 --port 102
```

WPF IED Simulator — **Open SCL…** loads the model, **Start** opens the read-only
MMS server on `127.0.0.1:102`, and incoming MMS requests appear in the activity
grid. Port 102 is the IEC 61850 default and usually requires running the app elevated.

## Validation

`MmsVirtualIedServerTests` connects a real TCP socket client to the running
server and asserts the full path end to end: handshake, `GetNameList` discovery
returning the SCL logical-device name, a successful `Read`, and a `Write` that the
read-only guard rejects. A second test proves a client disconnecting mid-session
does not disturb the listener for the next client.

```powershell
dotnet test .\ARIEC61850.slnx -c Release
```

## Scope boundary and next steps

- The association negotiation now mirrors the incoming Session Connect parameters,
  returns accepted presentation-context results, and carries the negotiated MMS
  context id into the confirmed-request response loop. This is validated against
  the native client and custom context-id tests. Interoperability with independent
  third-party IEC 61850 clients remains a manual laboratory gate.
- The current server is read-only and implements the browse/read/DataSet-directory
  subset required for the first discovery path. It does not yet claim full IEC
  61850 server conformance, reporting, GOOSE/SV publication, control, write,
  fragmentation, authentication, or TLS support.
- After negotiation: engine-side unbuffered reports (URCB), then buffered reports
  with EntryID/overflow, then — only with explicit lab-mode confirmation —
  control/write.
- The server stays read-only by design; do not add write/control behavior before
  the reporting model is complete.
