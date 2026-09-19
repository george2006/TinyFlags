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

## Completed slice: 4.2 - generated catalogs and root composition

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

## Completed slice: 5 - dependency injection

The user approved slice 4.2 and requested its commit and continued work. Existing code already
provides generated access constructors taking FeatureValues and per-assembly definition
contributions. The remaining DI behavior is one shared store per container and typed access
registration from initialized assemblies through `services.AddTinyFlags()`.

The user approved continuing with the proposed callback contract and runtime step. Agreed steps:

1. Runtime DI plumbing: add the DI abstractions dependency, `AddTinyFlags`, and an overload
   of `AddContribution` accepting `Action<IServiceCollection>` alongside the catalog type and
   definitions. Preserve the existing metadata-only overload. Register FeatureValues once
   per container and apply a snapshot of registration callbacks outside the registry lock.
   Test with real service collections/providers: repeat registration, independent containers,
   preservation of an explicitly supplied store, and existing instances observing updates.
2. Generated registrations: include access-class identities in the generation plan and emit
   typed singleton factories using the container's FeatureValues. Pass the generated callback
   from each module initializer. Verify resolution and shared updates across separately
   compiled libraries and a host, including repeated AddTinyFlags calls.

The approved callback is a registration contract. Its current
consumer is the root DI registration method; generated code knows the concrete access types
that the runtime cannot reference. A standard delegate avoids a custom contribution interface.
The simpler alternative is manual registration of every generated access class in the host,
which loses the intended automatic multi-assembly composition.

Use singleton access classes because getters read the shared store on every call. Use TryAdd
registrations so repeated calls do not duplicate services or replace explicit registrations.
Assemblies must initialize before registration; this slice adds no assembly scanning, dynamic
service-provider mutation, networking, or packaging. Each step stops for review.

Step 5.1 is implemented: TinyFlags references DI abstractions, AddTinyFlags registers the shared
store with TryAddSingleton, and bootstrap contributions keep definitions and callbacks together.
The first registration for a contribution type wins across both overloads. Apply takes a
snapshot under the registry lock and executes callbacks outside it. Repeated AddTinyFlags calls
reapply callbacks; callbacks must use repeat-safe registrations. This also allows contributions
registered later to be applied before building a container. Callback failures propagate and do
not roll back service registrations already made; repeat-safe callbacks can be retried.

Verification: `dotnet test TinyFlags.slnx -warnaserror` passed 133 tests (112 generator/integration,
21 runtime). Nine new scenarios use real service providers and generated access classes in an
isolated runtime. They verify sharing across scopes, updates to existing instances, independence
between containers, an explicit store, duplicate contribution identity, metadata-only precedence,
late contributions, null arguments, and retry after a callback failure. No mocks or reset hooks.

Status: step 5.1 implemented, verified, and approved by the user's instruction to continue.

Step 5.2 is implemented: the catalog plan includes sorted access-class names, including empty
providers. Its equality includes those names, so renaming an empty provider invalidates its
registration. The emitter generates typed TryAddSingleton factories using FeatureValues and
passes the registration method through the module initializer. No Roslyn dependencies enter
planning or emission. Existing runtime callback tests now register a concrete test consumer of
the generated access class, keeping manual callback behavior distinct from automatic registration.

Verification: `dotnet test TinyFlags.slnx --no-restore -warnaserror` passed 138 tests
(117 generator/integration, 21 runtime). New checks cover root plus transitive library resolution,
shared updates and singleton scopes across assemblies, repeated registration, reversed library
load order, partial/empty/escaped declarations, explicit access instances, invalid providers,
and incremental invalidation after renaming an empty provider.

Status: step 5.2 implemented, verified, and approved by the user's instruction to continue.

## Completed slice: 6 - local NuGet consumer

Approved scope: package the runtime and source generator together and verify a separate local
consumer. TinyFlags.csproj builds the generator through a private project reference and packs
its DLL under analyzers/dotnet/cs, the runtime under lib/net8.0, and README.md at the package
root. The generator remains a compiler asset, not a runtime dependency. No public publishing.

Implemented tests/TinyFlags.PackageTests/Verify-Package.ps1 with a host and library fixture.
The script packs a unique prerelease version, checks package assets and dependencies, copies
the fixtures into an isolated artifacts directory, and restores through a local feed using a
fresh package cache. Each fixture consumes TinyFlags through PackageReference; the only project
reference is the host-to-library relationship. NuGet source mapping requires TinyFlags to
come from the local feed. The fixtures stay outside the main solution because their build
requires the freshly packed package; run the verification script explicitly.

Verification: the package consumer builds in Release with zero warnings/errors, resolves flags
from the host and initialized library, checks defaults, singleton identity, repeat registration,
live snapshot updates, root definitions, and absence of the generator DLL at runtime. An
intentional invalid declaration fails a subsequent build with TFG002 from the packaged generator.
All 138 existing tests also pass with `dotnet test TinyFlags.slnx -warnaserror`.

Status: implemented, verified, and approved by the user's instruction to commit and move on.
No server or synchronization added.

## Current feature: background server registration

Branch: `feature/server-registration`. The user requested design review before implementation,
then explicitly requested TinyDispatcher and vertical slices for the API. TinyEvents is to be
used only when a concrete need arises; it is not part of the initial registration transaction.

The full proposal is in [server-registration-design.md](docs/server-registration-design.md).
It supersedes the earlier client-first sketch: implement the server's registration command and
persistence, expose its authenticated endpoint, then connect the HTTP client and background
worker. Registration runs in the worker after a startup signal and never waits on server I/O
on the application's startup path. Value synchronization remains the following feature.

The user approved starting slice 1 with PostgreSQL + EF Core and one server project. Remaining
slices cover environment-scoped create-only definitions, transactionally safe replay/concurrency,
API-key scope, and the concrete types documented in the proposal.
TinyDispatcher use and the vertical-slice direction are already user requirements.

The user also requires Docker and a dedicated `TinyFlags.Server.IntegrationTests` project.
Every exposed server interaction must have integration coverage through HTTP, real authentication,
TinyDispatcher, and PostgreSQL. Follow TinyEvents' Testcontainers approach, apply actual migrations,
isolate test databases, and fail visibly when Docker is unavailable. The design document includes
the proposed fixtures, coverage matrix, and multi-instance concurrency tests.

Status: database foundation and registration command approved and committed; concurrency approved;
API-key authentication implemented, verified and approved for commit with slice 3.
No endpoint, worker or transport code yet.

The user requested smaller, quick-to-review slices. The detailed design now specifies:

1. Docker/database foundation and real persistence tests.
2. TinyDispatcher registration command: create and replay.
3. Atomic conflicts and concurrent registration.
4. API-key authentication against the real database.
5. Authenticated endpoint with complete HTTP integration coverage.
6. One HTTP registration attempt from the concrete SDK client.
7. Non-blocking startup signal and single-attempt worker lifecycle.
8. Retry, cancellation and recovery.
9. Packaged end-to-end registration against the server and Docker database.

Each slice stops for review. This breakdown replaces the earlier broader five-step proposal.

### Slice 1: database foundation - implemented and approved

Added TinyFlags.Server and TinyFlags.Server.IntegrationTests to the solution, targeting net10.0.
The existing SDK remains net8.0. Project and ProjectEnvironment use application-assigned GUIDs;
EF mappings enforce environment-name uniqueness within a project, required names, and a foreign
key that restricts deletion of a project with environments. Environment names use C collation.
The actual generated migration and model snapshot are checked into the working tree.

PostgreSqlFixture owns a digest-pinned PostgreSQL 16 Docker container and creates a fresh database
per test, applying real migrations. Container disposal reclaims those test databases. Docker
must be running; integration tests do not silently skip. A separate compose file provides a
persistent local development database on localhost:54324. Program configures DbContext without
running migrations at startup. The local dotnet-ef tool is pinned to 10.0.4, matching EF runtime.

Updated Testcontainers.PostgreSql to 4.15.0: its dependency set resolves the SSH.NET vulnerability
warning encountered with 4.12.0. Restore/build/test now pass with warnings treated as errors.
See the [upstream package dependencies](https://www.nuget.org/packages/Testcontainers/4.15.0).

Verification on 2026-09-19: `dotnet test TinyFlags.slnx -warnaserror` passed 144 tests
(117 generator/integration, 21 local runtime, 6 server integration). Server tests verify persisted
project/environment round-trips, migration reapplication and model alignment, same-project name
uniqueness, cross-project name reuse, missing-project rejection, restricted project deletion,
and independent databases. `docker compose config --quiet` also passed.

Approved by the user's instruction to proceed to slice 2; committed with slice 2 in `50ee8e9`.

### Slice 2: register definitions - implemented and approved

Added TinyDispatcher 1.3.0-beta.3, matching Feelings, and composed generated registrations in
Program. Features/RegisterDefinitions owns the command, input, handler, batch and persisted entity.
The user requested the OOP extraction during review: DefinitionInput converts itself to a
RegisteredFeature, RegistrationBatch validates/normalizes the full batch and determines missing
definitions, and RegisteredFeature compares definition contents. The handler orchestrates the
batch, reads existing definitions directly through DbContext, and saves missing rows once.
No new interface, mapper service, repository or dispatcher wrapper.

Same-kind replay retains the original default and creation timestamp; omitted flags remain.
Identical duplicates collapse, conflicting duplicates and malformed defaults/keys reject the
whole batch before writes. At this milestone, existing incompatible kinds raised
InvalidOperationException and racing initial inserts could fail on the unique key. Slice 3
below replaces that guard with the designed feature-specific exception and locking behavior.

Added the RegisteredFeatures migration: composite environment/key primary key with C collation,
environment foreign key, creation timestamp, and a check constraint enforcing matching boolean
or string default columns. Keys are limited to 400 characters; blank keys are rejected and exact
case is preserved. There is no dependency on the TinyFlags SDK from the server.

Verification: `dotnet test TinyFlags.slnx --no-restore -warnaserror` passed 159 tests
(117 generator/integration, 21 SDK runtime, 21 server integration), including after the OOP
refactor with the same behavior tests. Fifteen new cases use real TinyDispatcher with generated
registrations and a fresh DI scope per dispatch. They cover typed/empty defaults, timestamps,
replay, changed defaults, omitted flags, empty batches, project/environment isolation, key casing,
equivalent/conflicting duplicates, malformed defaults/keys, existing kind mismatch and the
database check constraint. Migration/model alignment is covered by the existing integration test.

Approved by the user's instruction to commit and move to slice 3.

Slices 1 and 2 committed as `50ee8e9`.

### Slice 3: atomic conflicts and concurrent registration - implemented and approved

RegisterDefinitionsHandler validates the batch, opens a Read Committed transaction, and locks
the environment row before reading existing definitions. All registration instances follow this
ordering. Missing definitions are saved and committed together; failure disposes the transaction.
Empty batches remain a no-op. RegistrationBatch reports all kind conflicts through the agreed
FeatureKindConflictException with an immutable, ordinally sorted key list. No schema change.

Verification: `dotnet test TinyFlags.slnx --no-restore -warnaserror` passed all 165 tests
(117 generator/integration, 21 SDK runtime, 27 server integration), with no skips.
Six new tests use real TinyDispatcher and PostgreSQL. They verify simultaneous identical
registrations, one complete winner for incompatible batches, independent environments while
one is blocked, cancellation, all conflicting keys, no partial writes and subsequent registration
after failure. Competing requests use independent service providers; the tests observe lock waits
in pg_stat_activity before releasing a held row lock. Registration tests share one dispatcher
bootstrap across partial class files, avoiding the duplicate-bootstrap diagnostic.

Following the concurrency review, two additional cases cover overlapping batches with different
defaults (consistent shared defaults, both sets of unique keys, and unchanged defaults/timestamps
on replay) and a waiting registration surviving another transaction's failure after insertion.
The failure case stages an uncommitted writer directly through DbContext, observes the handler
waiting, causes a real PostgreSQL division-by-zero error, and rolls back. The handler then creates
the same key with a different kind; none of the aborted writer's rows remain. This verifies recovery
behind an aborted writer, not fault injection into the handler's own commit/disposal path.

No HTTP endpoint, authentication or worker in this slice. Approved by the user's instruction to
move to API-key authentication. HTTP coverage follows with the endpoint in slice 5.

### Slice 4: API-key authentication - implemented and approved

The user agreed to separate application credentials from future dashboard identities and roles.
Implement the agreed environment-scoped API key and registration permission, using ASP.NET's
authentication scheme and authorization policy. No generic permission framework or custom interfaces.

ClientApiKey.Issue generates a 256-bit random opaque secret, returning it separately from the
entity. Only its SHA-256 hash is persisted. Revoke records an idempotent revocation timestamp.
ClientApiKeyAuthenticationHandler accepts a single Authorization: Bearer credential, validates
its format, and queries the real database without tracking. Claims contain the key ID, environment,
project and granted registration permission; no raw credential is carried into claims or errors.
ClientApiKeyAuthentication owns scheme/policy configuration used by Program and integration tests.
The registration policy explicitly authenticates the client-key scheme, keeping dashboard identity
claims from granting machine permissions. Permission is currently a concrete boolean grant on the
key; dashboard roles and future permissions remain separate feature discussions.

Added AddClientApiKeys migration with unique token hash and a restricted environment foreign key.
Authentication reads on every request; committed revocation is observed by subsequent lookups,
but does not cancel already-authenticated requests. Replacement keys can coexist for rotation.
There is no key management endpoint, login flow, expiry lifecycle or registration route yet.

Verification: `dotnet test TinyFlags.slnx --no-restore -warnaserror` passed all 181 tests
(117 generator/integration, 21 SDK runtime, 43 server integration), with no skips.
Sixteen new cases cover exact scope/claims across projects and environments, hash-only persistence,
multiple keys, missing/malformed/unknown/tampered/duplicate credentials, permission denial,
revocation and replacement keys, dashboard identity isolation, 401/403 without redirects, and
database uniqueness/foreign-key constraints. They use real authentication services and the
ASP.NET policy evaluator with PostgreSQL, without mocks. The initial test setup lacked routing
services required by authorization; adding the real routing services fixed DI validation.
Existing migration tests verify model alignment. Full HTTP middleware coverage is slice 5.

Approved by the user's instruction to commit and move on. Next slice exposes the authenticated
registration endpoint through TinyDispatcher and verifies the complete HTTP path.

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

Startup requirement explicitly requested by the user: registration and initial synchronization
with the server must not delay process startup. Startup signals the background worker and
continues without waiting for network I/O. The worker owns server registration, synchronization,
and retries. Reads use declared defaults until a successful initial snapshot arrives; subsequent
failures retain the last known values. Design the signaling mechanism in that future feature;
do not introduce it into the current DI slice.
