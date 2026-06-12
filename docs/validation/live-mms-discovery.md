# Live MMS Discovery Validation

Date: 2026-06-12

This note records the first useful native MMS discovery pass in ARIEC61850.
The implementation is clean-room code in `src/AR.Iec61850` and is exposed by
the CLI command `mms-discover`.

## Command

```powershell
dotnet run --project .\apps\AR.Iec61850.Cli -- mms-discover 192.16.1.157 --port 102 --timeout-ms 20000 --max-report-probes 16 --raw-limit 30
```

## Result

```text
Association: MmsInitiated
ACSE/MMS profile: BalancedApTitle
ACSE/MMS response: accepted, 178 byte
logical devices: 4
raw variables: 10122
DataSets: 1
report controls: 286
BRCB: 8
URCB: 278
```

Discovered logical devices:

```text
OCR7SR12CTRL: variables=3152 datasets=0
OCR7SR12DR: variables=281 datasets=0
OCR7SR12MEAS: variables=1770 datasets=0
OCR7SR12PROT: variables=4919 datasets=1
```

Discovered DataSet:

```text
OCR7SR12PROT/LLN0.DataSet raw=LLN0$DataSet
```

Example RCB evidence:

```text
BRCB OCR7SR12PROT/LLN0.BR.brcbA01 datSet=OCR7SR12PROT/LLN0.DataSet rptID=OCR7SR12PROT/LLN0$BR$brcbA01 confRev=1 intgPd=0 rptEna=false
BRCB OCR7SR12PROT/LLN0.BR.brcbB01 datSet=OCR7SR12PROT/LLN0.DataSet rptID=OCR7SR12PROT/LLN0$BR$brcbB01 confRev=1 intgPd=0 rptEna=false
```

## Scope

This validates:

- TCP/TPKT/COTP connection.
- ACSE/MMS association.
- MMS `GetNameList` domain discovery.
- MMS `GetNameList` named-variable discovery.
- MMS `GetNameList` named-variable-list discovery.
- DataSet and RCB inventory mapping.
- Bounded Confirmed-Read probing for selected RCB attributes.

This does not yet validate:

- Report enable/disable writes.
- General interrogation trigger.
- InformationReport receive/decode.
- Buffered report entry recovery.
- Multi-vendor interoperability.
