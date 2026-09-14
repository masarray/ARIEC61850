# SCL-assisted MMS connect — Step 3

Status: **implemented + deterministic unit coverage; live IED interoperability not yet validated**.

Step 3 is the first opt-in runtime slice for the SCL-assisted connect path. It deliberately stays smaller than full live discovery.

## Contract

```text
trusted SCL
    -> Step 1: parse MMS endpoint + ISO association identity
    -> Step 2: build exact COTP + Session/Presentation/ACSE/MMS plan
    -> Step 3: TCP/COTP/ACSE/MMS associate with that exact plan
    -> GetNameList(Domain, VMD) only
    -> compare online domains with SCL logical devices
    -> keep the MMS association alive for later work
```

The ordinary `MmsClientSession.ConnectAsync()` and full `DiscoverAsync()` paths remain unchanged. Step 3 is reached only through `ConnectSclAssistedAsync(...)`.

## Domain design inventory

`SclMmsDomainInventoryReader` scopes the design model to one exact `IED/AccessPoint/Server` and projects each direct `LDevice` into its expected MMS domain:

- use `LDevice@ldName` when explicitly present;
- otherwise use `IED@name + LDevice@inst`;
- collapse duplicate design-domain declarations with a warning;
- fail closed when the selected IED, AccessPoint, Server, or resolvable logical-device inventory is absent.

This design inventory is not treated as proof that the device is online. Online presence still comes from the associated IED through MMS `GetNameList` for object class Domain at VMD scope.

## Reconciliation policy

The reconciliation result keeps these sets separately:

- expected SCL domains;
- observed online domains;
- matched domains;
- missing expected domains;
- extra observed domains.

Policy:

```text
missing expected domain -> incompatible / DomainMismatch
extra observed domain    -> preserve as evidence; do not mutate SCL
all expected present     -> compatible
all expected present and no extra -> exact match
```

A mismatch does not rewrite the trusted SCL model and does not automatically launch full discovery. If the transport remains healthy, the association stays open so the application can show diagnostics and choose an explicit next action.

## Exact-plan guard

Before opening a socket, Step 3 rebuilds the Step-2 COTP and ACSE/MMS bytes from the typed plan and verifies that they still match the prebuilt bytes. A mutated or internally inconsistent plan is rejected as `InvalidPlan` without network side effects.

The existing no-argument `CotpClient.ConnectAsync(...)` path still builds the historical default COTP CR bytes. The new overload accepts `CotpConnectParameters` only for explicitly parameterized callers.

## Explicit non-goals

Step 3 does **not** perform:

- NamedVariable `GetNameList`;
- NamedVariableList/DataSet discovery;
- `GetVariableAccessAttributes` / GVAA;
- FC-root initial value reads;
- report discovery or RCB enable;
- dynamic DataSet creation;
- writes or controls;
- automatic fallback to full discovery.

The initial FC-root read planner/executor is Step 4. Its batching and scheduling contract remains separate from the MMS initiate negotiation: maximum variable references per Read and maximum outstanding calling are not the same setting.

## Validation

Deterministic tests cover:

1. scoped IED/AccessPoint logical-device projection including explicit `ldName`;
2. fail-closed missing Server handling;
3. missing expected domain => incompatible;
4. extra online domain => evidence-only and still compatible;
5. invalid design/plan input returns a typed `InvalidPlan` result without TCP side effects.

Repository validation remains:

```powershell
dotnet restore .\ARIEC61850.sln
dotnet build .\ARIEC61850.sln -c Release
dotnet test .\ARIEC61850.sln -c Release --no-build
.\scripts\verify-source-clean.cmd
```

Live IEC 61850 IED validation is intentionally a later evidence gate. Until that happens, Step 3 must not be described as field-validated or universally interoperable.
