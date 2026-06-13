# Public Release Checklist

Run this before pushing to a public repository.

## Source hygiene

```powershell
git status --short
dotnet clean .\ARIEC61850.sln
.\scripts\clean-local-artifacts.cmd
.\scripts\verify-source-clean.cmd
```

Confirm these folders are not staged:

```text
.vs/
bin/
obj/
out/
.artifacts/
artifacts/
evidence/
captures/
```

## Build validation

```powershell
dotnet restore .\ARIEC61850.sln
dotnet build .\ARIEC61850.sln -c Release
dotnet test .\ARIEC61850.sln -c Release --no-build
```

## Public text scan

Search for accidental unrelated product names, generated evidence, private IP notes, customer names, relay serials, and internal audit text.

## Release package

```powershell
.\scripts\publish-windows-singlefile.cmd -Version 0.1.0
.\scripts\verify-release-package.cmd -PackagePath .\.artifacts\release\ARIEC61850-SvPublisher-v0.1.0-win-x64-single-exe.zip -Version 0.1.0
```

Do not commit anything from `.artifacts/release`.
