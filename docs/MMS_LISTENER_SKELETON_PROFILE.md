# MMS Listener Skeleton Profile

The MMS listener skeleton is a server-side milestone that validates the TCP listener lifecycle before the full IEC 61850 MMS PDU decoder is attached.

It is intentionally conservative:

- loopback-only by default;
- read-only virtual IED semantics;
- JSON-line probe harness for deterministic automated testing;
- no live IEC 61850 MMS claim yet;
- no write/control operation allowed.

## Why this milestone exists

The previous read-only server profile proved the virtual IED model and high-level service handler offline. This milestone adds the first live transport boundary:

```text
TcpListener
→ accepted client session
→ request decode harness
→ read-only service dispatch
→ response encode harness
→ write guard verification
→ Markdown/JSON evidence
```

This gives the engine a safe bridge between simulator-backed server semantics and the future TPKT/COTP/ACSE/MMS listener.

## Run the self-probe

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-listener-skeleton-profile --port 0 --output .\.artifacts\out\mms-listener-skeleton.md --json .\.artifacts\out\mms-listener-skeleton.json
```

`--port 0` asks Windows to allocate an ephemeral loopback port. The command starts the listener, opens a local client, sends deterministic probe requests, verifies responses, and exits.

## Expected probes

The default self-probe checks:

- logical-device directory;
- logical-node directory;
- point read;
- DataSet read;
- write rejection.

A successful run should report:

```text
ready=true
connections=1
requests>=5
ok>=4
fail>=1
writeGuardVerified=true
```

The failed response is expected because the write probe must be rejected in read-only mode.

## Scope boundary

This is not yet a full IEC 61850 MMS server. The live MMS server milestone still needs:

- TPKT listener framing;
- COTP connection confirm;
- ACSE associate response;
- MMS initiate response;
- MMS confirmed-request dispatch;
- BER/MMS request decoding;
- confirmed-response encoding.

The listener skeleton proves that the server lifecycle, service dispatch, and evidence path are ready before those protocol layers are attached.
