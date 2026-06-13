## Summary

- 

## Validation

- [ ] `dotnet build .\ARIEC61850.sln -c Release`
- [ ] `dotnet test .\ARIEC61850.sln -c Release --no-build`
- [ ] `scripts\verify-source-clean.cmd`

## Safety / clean-room checklist

- [ ] No generated build output, captures, evidence, or release artifacts committed.
- [ ] No confidential SCL/PCAP/customer data committed.
- [ ] No restrictive-license source code copied or mechanically ported.
- [ ] Active network behavior is documented and guarded.
