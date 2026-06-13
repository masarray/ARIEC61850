# Public Release Checklist

Run this before pushing to a public repository.

## Source hygiene

```powershell
git status --short
dotnet clean .\ARIEC61850.slnx
```

Confirm these folders are not staged:

```text
.vs/
bin/
obj/
out/
artifacts/
evidence/
captures/
```

## Build validation

```powershell
dotnet restore .\ARIEC61850.slnx
dotnet build .\ARIEC61850.slnx -c Release
dotnet test .\ARIEC61850.slnx -c Release --no-build
```

## Public text scan

Search for accidental unrelated product names, generated evidence, private IP notes, customer names, relay serials, and internal audit text.

## Release package

```powershell
pwsh .\scripts\publish-windows-singlefile.ps1 -Version 0.1.0
pwsh .\scripts\verify-release-package.ps1 -PackagePath .\artifacts\release\ARIEC61850-SvPublisher-v0.1.0-win-x64-single-exe.zip -Version 0.1.0
```

Do not commit anything from `artifacts/release`.
