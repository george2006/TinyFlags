# TinyFlags

## Completed slice: 1 - declarations and diagnostics

Approved by the user: implement the first slice of the revised six-slice feature below.

Goal: recognize marked provider classes, extract boolean/string names, identities and constant
defaults, and report unsupported declarations through the incremental generator.

Implemented components: marker project, generator entry point, discovery, analysis, validation,
definition models, and tests using real Roslyn compilations. Only `IFeatureProvider` is a new
interface; the implementation uses concrete types. Planning/emission folders are not created
before they have a job.

The first supported shape is documented in `README.md`. It includes partial classes and
constant getters/initializers, but excludes inheritance, nested/generic/file-local types and
records. Invalid providers do not contribute a partial model.

Verification: `dotnet test TinyFlags.slnx --no-restore -warnaserror` passed all 42 tests.
Roslyn symbols remain in `Analysis`; models and validation are free of Roslyn dependencies.
Status: implemented, verified, and approved by the user's instruction to continue.

## Completed slice: 2 - local values

Implement the approved concrete `FeatureValues`: typed local reads, default fallback and
atomic snapshot replacement. Missing keys or mismatched value types use the caller's default.
Updates copy and validate the supplied values before publication, so invalid updates preserve
the previous snapshot and later caller mutations cannot change stored values.

Use a standard dictionary of boolean/string values; no new interfaces or custom value contracts.
Verify observable reads and replacement with real values. No network, DI, or source emission.
Verification: `dotnet test TinyFlags.slnx -warnaserror` passed 52 tests (42 generator, 10 runtime).
The runtime checks include replacement, defaults, caller mutation isolation, rejection without
partial publication, and concurrent reads/updates. Individual reads observe one snapshot;
multiple reads are not a transaction. No server revision ordering is implemented in this slice.
Status: implemented, verified, and accepted by the user's instruction to fix incrementality
and commit the current work. Slice 3 has not started.

## Completed slice: generator incrementality

Approved: fix per-provider analysis and model equality; commit if tests pass.

The semantic transform now returns one plain model per provider. Partial declarations choose
one candidate while analyzing all members. Validation runs independently for each provider;
there is no pre-analysis collection or compilation-wide analysis stage. Models compare scalar
values and collection contents, with corresponding hash codes.

Verification: `dotnet test TinyFlags.slnx --no-restore -warnaserror` passed all 64 tests
(54 generator, 10 runtime). Twelve incremental tests reuse a driver across edits, covering
unrelated edits, individual defaults, external constants, partials, removing markers, and
diagnostic removal/location updates. Moving a valid declaration preserves its validated model;
changing only helper code reuses validation.

Roslyn can still repeat semantic transforms after a compilation edit. Value equality stops
unchanged results from invalidating downstream validation. Diagnostic reporting combines with
the current compilation only at the output boundary to bind cached issues to current trees.
This verifies incremental behavior, not throughput or IDE performance benchmarks.

Status: implemented, verified, and committed in `a00f008`.

The interface marker also requires semantic candidate checks; an attribute enables Roslyn's
optimized attribute lookup but would change the agreed declaration API. Keep the interface
unless the user approves a contract change.

## Current slice: explicit discovery phase

Discovery is the initial syntactic filter, followed by semantic Analysis. Move the candidate
filter into `Discovery` and let the generator entry point connect both callbacks. Keep marker
resolution, partial selection, and every Roslyn symbol inside `Analysis`.

Rename the validation output to `FeatureValidationResult` and the pipeline variable to
`validation` so names match their phases. No new contracts or generated access classes.

Verification: `dotnet test TinyFlags.slnx --no-restore -warnaserror` passed all 64 existing
tests, including incremental caching and partial behavior. Symbol use remains inside Analysis.
Status: implemented and verified; included in the requested commit. Stop for review before
starting generated access classes.

## Completed slice: 0 - workspace bootstrap

Requested: a new `TinyFlags` folder, solution, `src/`, and `tests/` under the shared repos folder.

Changes:

- `TinyFlags.slnx` with source/test solution folders and the working documents.
- `src/` and `tests/`, with placeholders so empty directories can be tracked later.
- The working agreement and agent instructions.
- A minimal ignore file for local build and editor artifacts.

Verification: check that `dotnet sln TinyFlags.slnx list` reads the solution and that the
required directories and documents exist. No executable behavior or tests in this slice.

Verification result: `dotnet sln TinyFlags.slnx list` succeeded with no projects, as expected
for this scaffold. Source/test directories and solution documents are present.

Status: scaffold created, verified, and approved by the user's instruction to continue.
The revised feature slicing below was subsequently approved.

## Product intent agreed in conversation

- An open-source NuGet client and source generator, with a paid server and a small dashboard.
- Users group flag declarations in classes; each supported property represents a flag with
  a default. `IFeatureProvider` is the agreed empty declaration marker, implemented in slice 1.
- Compilation prepares the catalog locally. Network registration occurs at deployment or
  application startup, never as a side effect of compilation.
- Registration introduces new definitions without overwriting values already configured.
- A client API key identifies its project and environment. Reading values and registering
  definitions are separate permissions from changing existing values or managing members.
- The client evaluates locally from its last known configuration, using the declared default
  when no synchronized value exists.
- Server, SQL persistence, dashboard, and infrastructure reliability are later feature
  discussions, not part of this bootstrap.

## Agreed feature: typed declarations and local access

Goal: compile boolean/string declarations into typed access classes and a registration catalog,
with local value resolution. Compilation never executes declaration code or contacts a server.

Input: classes implementing `IFeatureProvider`, with public instance read-only `bool` or
`string` properties and constant defaults. A declaration named `Checkout` will generate
`CheckoutFeatureFlags` in the same namespace; the generated class does not implement the marker.

Agreed identity: fully qualified provider type name plus property name. Renaming either
creates a different identity; explicit stable identifiers can be discussed before widening
the public contract.

Agreed slices:

1. Declarations: discover and analyze providers, extract types/defaults/identities, and diagnose
   unsupported forms. Validate with real Roslyn compilations.
2. Local resolution: implement concrete `FeatureValues`, default fallback, and atomic snapshot
   replacement. No HTTP dependency.
3. Generated access: plan and emit typed `NameFeatureFlags` classes. Compile and execute them
   to verify that existing instances observe changes to `FeatureValues`.
4. Generated catalog: expose definitions for server registration without reflection. Agree the
   catalog contract when starting that slice.
5. DI registration: `AddTinyFlags` wires generated classes to the shared store, including
   declarations from multiple assemblies.
6. NuGet consumer: package the generator and client and verify a separate local consumer.

Current project layout:

```text
src/TinyFlags
src/TinyFlags.SourceGen
tests/TinyFlags.SourceGen.Tests
```

Runtime/tests target net8.0 and the generator targets netstandard2.0 with Roslyn 4.8.0, following
the existing suite. The test dependencies match TinyValidations' generator test project.

HTTP registration, API keys, background synchronization, server, dashboard, and deployment
remain later features. This feature establishes declarations and local typed consumption.

## Approved runtime direction for later slices

Generated getters delegate to a concrete shared `FeatureValues` instance supplied by DI. They
read the current local snapshot each time and use the declared default when no synchronized
value is available. Generated getters do not perform network requests or capture values once
in their constructors.

The future synchronization worker replaces the snapshot after successful API reads and keeps
the last known values after failures. `FeatureValues` and the marker were discussed and approved;
any additional abstraction still requires explanation and permission under the working agreement.
