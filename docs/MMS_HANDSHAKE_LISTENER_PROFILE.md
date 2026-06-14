# MMS Handshake Listener Profile

The MMS handshake listener profile is a loopback server-side transport milestone. It validates that the stack can accept a TCP connection, receive a TPKT-wrapped COTP Connection Request, return a COTP Connection Confirm, receive a COTP Data TPDU, and inspect the ACSE/MMS association payload.

This profile is intentionally narrow. It does **not** claim to be a complete MMS server yet and it does **not** send an ACSE AARE or MMS initiate response. It exists to harden the live listener/session lifecycle before the read-only MMS service handler is connected to real MMS confirmed requests.

## Run

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-handshake-listener-profile --port 0 --output .\.artifacts\out\mms-handshake-listener.md --json .\.artifacts\out\mms-handshake-listener.json
```

`--port 0` uses an ephemeral loopback port, so the command is safe for local CI/dev machines and does not require binding to TCP/102.

## Evidence gates

The profile reports these gates:

- TPKT exchange verified
- COTP connection confirmed
- COTP Data TPDU observed
- ACSE/MMS association payload observed

A ready profile means the listener-side OSI transport skeleton is healthy enough for the next milestone: ACSE AARE and MMS initiate response handling.
