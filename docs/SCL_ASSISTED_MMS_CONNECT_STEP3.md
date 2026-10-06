# SCL-assisted MMS connect — Step 3

Status: **implemented with deterministic tests included; live IED interoperability not yet validated**.

Step 3 is the first opt-in runtime slice for the SCL-assisted connect path. It deliberately stays smaller than full live discovery.

## Contract

```text
trusted SCL
    -> Step 1: parse MMS endpoint + ISO association identity
    -> Step 2: retain exact-plan capability for complete addressing
    -> Step 3: classify association fields
         explicit valid -> immutable constraint
         unspecified    -> bounded engine-profile resolution
         invalid/conflict -> fail closed
    -> serial fresh-transport candidate negotiation
    -> first accepted MMS association
    -> GetNameList(Domain, VMD) only
    -> compare online domains with SCL logical devices
    -> keep the MMS association alive for later work
```

The ordinary `MmsClientSession.ConnectAsync()` and full `DiscoverAsync()` paths remain unchanged. Step 3 is reached only through `ConnectSclAssistedAsync(...)`.

## Incomplete association addressing

A ConnectedAP that omits an association parameter is not equivalent to one that declares malformed or conflicting data.

`SclAssistedMmsAssociationCandidateResolver` classifies each critical field as:

- `PresentValid` — explicit SCL evidence; candidates must match it exactly;
- `Unspecified` — no source value; the engine may resolve it from one of its bounded interoperability profiles;
- `Invalid` — explicit malformed value; fail closed before network I/O;
- `Ambiguous` — conflicting duplicate critical declarations; fail closed before network I/O.

Candidate generation is deterministic and bounded. It does not form a Cartesian product of guessed selector/AP-title/AE values. Complete SCL keeps the exact plan first. For incomplete SCL, engine-owned profiles may supply only the unspecified fields, and accepted resolution is returned as provenance rather than written back into the source SCL.

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

## Plan/candidate guards

The legacy exact-plan overload remains byte-stable: before opening a socket it rebuilds the Step-2 COTP and ACSE/MMS bytes and rejects an internally inconsistent plan as `InvalidPlan`.

The smart overload accepts only a successful bounded resolution. Every candidate is validated as a legal COTP request plus client ACSE/MMS associate payload before network I/O. Candidates are attempted strictly serially, each from a fresh transport. Per-attempt timeout and caller cancellation are explicit, and attempt evidence is association-scoped.

The existing no-argument `CotpClient.ConnectAsync(...)` path still builds the historical default COTP CR bytes. Parameterized SCL candidates use the explicit `CotpConnectParameters` overload.

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
5. invalid design/plan input returns a typed `InvalidPlan` result without TCP side effects;
6. missing AP-title/AE qualifier produces bounded compatibility candidates instead of a pre-socket error;
7. explicit non-default selectors remain unchanged in typed candidates;
8. malformed and conflicting explicit association values fail closed;
9. complete-SCL exact candidate remains first and byte-stable.

Repository validation remains:

```powershell
dotnet restore .\ARIEC61850.sln
dotnet build .\ARIEC61850.sln -c Release
dotnet test .\ARIEC61850.sln -c Release --no-build
.\scripts\verify-source-clean.cmd
```

The claim may advance from **implemented** to **unit tested** only when those checks pass for the exact Step-3 head commit. Live IEC 61850 IED validation is intentionally a later evidence gate. Until that happens, Step 3 must not be described as field-validated or universally interoperable.