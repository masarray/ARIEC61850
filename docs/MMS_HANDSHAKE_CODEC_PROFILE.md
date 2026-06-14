# MMS Handshake Codec Profile

The MMS handshake codec profile is an engine-only validation artifact for the server-side roadmap. It validates the transport and association codec pieces that must be reliable before the read-only listener is upgraded from the current JSON-line harness to an IEC 61850 MMS listener.

It currently validates:

- TPKT frame encode/decode and length checks.
- COTP Connection Request decode.
- COTP Connection Confirm encode/decode.
- COTP Data TPDU encode/decode.
- ISO Session / ACSE / MMS association payload inspection.
- Existing client association profile readiness for future server-side routing.

Run the offline profile:

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-handshake-codec-profile --output .\.artifacts\out\mms-handshake-codec.md --json .\.artifacts\out\mms-handshake-codec.json
```

This command is fully offline. It does not open port 102 and does not emulate a full MMS server. Its purpose is to protect the next implementation gate:

```text
TCP listener
→ TPKT frame boundary
→ COTP CR/CC session setup
→ COTP Data TPDU
→ ISO Session / ACSE association payload
→ MMS initiate routing
→ read-only confirmed request dispatch
```

## Public wording

Use neutral wording such as `handshake codec profile`, `association payload inspection`, and `server transport readiness`. Do not describe this as a complete IEC 61850 server until the listener handles real TPKT/COTP/ACSE/MMS PDUs end-to-end.
