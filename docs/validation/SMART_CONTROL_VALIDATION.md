# Smart Control Validation Record

## Current validation state

| Area | State | Evidence |
|---|---|---|
| Source structure | Passed | control namespace, typed descriptors, exact builders, sequence executor |
| C# syntax scan | Passed | 309 C# files parsed with the C# tree-sitter grammar; no syntax-error nodes |
| XML/project integrity | Passed | 25 project/XML/XAML files parsed successfully |
| Unit-test source matrix | Added | 32 smart-control test methods plus receive-router subscription tests |
| .NET compile/test in this environment | Not executed | .NET SDK is not installed in the execution container |
| Windows clean build | Required | run the commands below |
| Live simulator interoperability | Required | no TCP/102 control-capable simulator was available here |
| Multi-vendor IED interoperability | Required | must be recorded per vendor/model/firmware |
| Formal conformance | Not claimed | requires recognized laboratory evidence |

## Clean build commands

```powershell
dotnet restore .\ARIEC61850.sln
dotnet build .\ARIEC61850.sln -c Release
dotnet test .\tests\AR.Iec61850.Tests\AR.Iec61850.Tests.csproj -c Release --no-build
.\scripts\verify-source-clean.cmd
```

## Focused control test command

```powershell
dotnet test .\tests\AR.Iec61850.Tests\AR.Iec61850.Tests.csproj `
  -c Release `
  --filter "FullyQualifiedName~SmartControlStackTests|FullyQualifiedName~MmsReceiveRouterTests"
```

## Live evidence fields

Record one row per command:

| Field | Required |
|---|---|
| IED vendor/model/firmware | yes |
| Object reference and CDC | yes |
| discovered `ctlModel` | yes |
| exact Oper/SBOw/Cancel signatures | yes |
| request intent and raw request hex | yes |
| confirmed MMS result and raw response hex | yes |
| command termination result | enhanced models |
| control error and AddCause | negative cases |
| status feedback reference/value/time | yes |
| SBO ownership and timeout behavior | SBO models |
| external client competition result | yes |
| operator safety/test-mode setup | yes |

## Release gate

Do not label the stack production-ready until the clean Windows build passes and the live matrix in `SMART_CONTROL_STACK.md` has captured repeatable evidence.
