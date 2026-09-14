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
and commit the current work.

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

## Completed slice: explicit discovery phase

Discovery is the initial syntactic filter, followed by semantic Analysis. Move the candidate
filter into `Discovery` and let the generator entry point connect both callbacks. Keep marker
resolution, partial selection, and every Roslyn symbol inside `Analysis`.

Rename the validation output to `FeatureValidationResult` and the pipeline variable to
`validation` so names match their phases. No new contracts or generated access classes.

Verification: `dotnet test TinyFlags.slnx --no-restore -warnaserror` passed all 64 existing
tests, including incremental caching and partial behavior. Symbol use remains inside Analysis.
Status: implemented, verified, committed in `d03722a`, and approved by the instruction to continue.

## Completed slice: 3 - generated access classes

Approved: generate `NameFeatureFlags` classes backed by `FeatureValues`, with explicit phases.
The user confirmed that `Generation/` contains planning and emission, coordinated by
`FeatureGeneration`. It consumes validated definitions and returns source text without Roslyn.

Implemented: public sealed access classes in the declaration namespace, a constructor receiving
the concrete store, typed bool/string getters and declared-default fallback. Each read consults
the current snapshot. Source emission escapes identifiers and string constants, chooses a
nonconflicting backing field, and handles properties hiding object members. Analysis detects
generated class name conflicts; validation reports `TFG004` and suppresses that provider.

Verification: `dotnet test TinyFlags.slnx --no-restore -warnaserror` passed 85 tests
(75 generator, 10 runtime). Generated consumers are compiled and executed to verify defaults,
snapshot updates, partial declarations, namespace isolation, escaped identifiers/string values,
backing-field collisions, object member hiding and null constructor input. Incremental checks
now cover generation reuse and removal when declarations become invalid or conflict.

Status: implemented, verified, and explicitly approved by the user at the end of the session.
No catalog, DI registration, networking, or NuGet packaging in this slice.

The session ended after slice 3. The user resumed work on 2026-09-14 and approved the catalog
contract and its two reviewable steps below.

## Completed slice: 4.1 - registration definition contract

Approved direction: one generated internal catalog per assembly, exposed as
`TinyFlags.Generated.ThisAssemblyFeatureCatalog.Definitions`. Entries carry a key, supported
kind and declared default; current values remain in `FeatureValues`.

Agreed steps:

1. Implement the runtime definition contract and review it.
2. Generate the immutable catalog and verify it by compiling and executing consumers.

Step 4.1 adds the concrete immutable `FeatureDefinition`, its `Boolean` and `String` factories,
and the `FeatureKind` discriminator. A private constructor prevents callers from creating a
kind/default mismatch. Keys must be nonblank; string defaults must be non-null (empty is valid).
Keys and defaults retain their supplied values. No wire format or serialization contract yet.

Verification: `dotnet test TinyFlags.slnx --no-restore -warnaserror` passed all 96 tests
(75 generator, 21 runtime). Eleven new cases verify typed defaults, exact string preservation,
blank/missing key rejection, and null string default rejection.

Status: implemented, verified, and explicitly approved by the user before step 4.2.

## Current slice: 4.2 - generated catalogs and root composition

Implemented local catalog: each assembly gets an internal
`TinyFlags.Generated.ThisAssemblyFeatureCatalog.Definitions`, with an immutable collection
of valid definitions ordered by key. An assembly with no valid flags has an empty catalog.
Planning collects only validated providers; analysis and access generation remain per provider.
The catalog plan uses value equality to preserve downstream cache reuse. Reserved catalog name
conflicts report `TFG005` and suppress catalog generation.

Verification: `dotnet test TinyFlags.slnx --no-restore -warnaserror` passed 124 tests
(103 generator, 21 runtime). Catalog tests compile and execute consumers to verify metadata,
immutability, ordering, defaults, partial declarations and escaping; they also check diagnostics,
omission of invalid providers, cache reuse, and updates after provider removal.

The user clarified that catalogs must contribute across assemblies and be composed at the
root, following the suite. Reviewed TinyEventsBootstrap and TinyValidationBootstrap: generated
module initializers register contributions, and the host consumes the composed registry.

Approved and implemented contract: concrete `TinyFlagsBootstrap` with
`AddContribution(Type, IReadOnlyList<FeatureDefinition>)` for generated code and
`GetDefinitions()` for the root. No extra interface. Generated module initializers register
each catalog by its actual type identity. Registration copies the supplied definitions; the
first registration per type wins. Composition returns an immutable snapshot sorted ordinally
by key, deduplicates equivalent definitions, and throws for conflicting kinds or defaults.
Conflicts fail during composition, not module initialization. The bootstrap does not load or
initialize unused referenced assemblies.

Tests compile separate libraries and a host and execute their generated contributions. They
cover root/local catalogs, reversed load order, transitive calls, repeated registration,
equivalent keys, and conflicting kinds/defaults. Additional behavior checks cover caller
mutation isolation, immutable earlier snapshots, invalid input, case-sensitive keys, and
concurrent registration/composition. Each executable scenario loads its own runtime instance;
there is no production reset hook or mocked registry.

Status: implemented, verified, and approved by the user's instruction to commit and move on.
No DI or HTTP yet.

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
