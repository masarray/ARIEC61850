# Troubleshooting

## `dotnet` is not recognized

Install the .NET 8 SDK and reopen the terminal.

## WPF project fails on non-Windows runner

The WPF project targets `net8.0-windows`. Build it on Windows or use the GitHub Actions workflow, which runs on `windows-latest`.

## Live adapter list is empty

Install Npcap, enable the correct adapter, and run from an elevated terminal when required by the machine policy.

## SV/GOOSE traffic does not appear on the target device

Check:

- isolated test network is connected;
- VLAN ID and priority match the lab setup;
- destination multicast MAC is accepted by the switch path;
- selected adapter index is correct;
- Windows firewall/security tools are not blocking raw traffic;
- the target device is configured to subscribe to the same SCL stream identity.

## MMS port connection fails

Check:

- target IP address;
- TCP port 102 reachability;
- local firewall;
- IED maximum client count;
- whether another engineering tool is already holding a session;
- subnet, gateway, and VLAN routing.

## Report enable fails

Use report planning first. Typical causes:

- RCB already enabled;
- RCB reserved by another client;
- DataSet reference incompatible with the selected RCB;
- write access blocked by IED setting/security;
- ConfRev/DataSet mismatch;
- timeout too short for the IED.
