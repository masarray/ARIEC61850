# Public Alpha Readiness Profile

`public-alpha-readiness-profile` is the top-level engine readiness gate for the developer-preview release line. It combines the strongest offline and loopback evidence available in the engine without requiring field hardware.

## What it validates

- Static SCL engineering profile can parse the release sample.
- Expected GOOSE/SV streams can be derived from SCL.
- Synthetic healthy process-bus observations bind cleanly to the SCL expected model.
- GOOSE diagnostics classify the baseline as healthy.
- Sampled Values diagnostics classify the baseline as healthy.
- Read-only MMS loopback server alpha completes model, association, native BER dispatch, and write-guard checks.

This command is intentionally conservative. It is a public-alpha gate, not a conformance claim.

## Command

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- public-alpha-readiness-profile `
  --scl .\samples\scl\minimal-station.scd `
  --output .\.artifacts\out\public-alpha-readiness.md `
  --json .\.artifacts\out\public-alpha-readiness.json
```

Use `--port 0` to keep the MMS loopback listener on an ephemeral local port.

## Output

The Markdown/JSON evidence includes:

- release gate summary;
- SCL capability snapshot;
- process-bus binding status;
- GOOSE/SV diagnostics status;
- read-only MMS loopback status;
- blocking/warning findings;
- explicit scope boundary.

## Public-alpha boundary

A passing profile means the engine foundation is ready for developer-preview packaging. It does not mean:

- full IEC 61850 conformance;
- production-ready MMS server;
- safe field control operation;
- complete file/log/setting/control service coverage.

Keep this command green together with build, test, and source-clean before tagging a public alpha release.
