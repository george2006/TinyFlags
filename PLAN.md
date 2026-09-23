# TinyFlags

## Current feature: value synchronization - slice 6 approved

Registration is complete; slices 8 and 9 were committed in b980cfc after 305 tests passed.
The approved design in docs/value-synchronization-design.md defines components, ownership, conditional
GET, environment revisions, permissions and seven small slices. It proposes one synchronization
worker as the SDK's only FeatureValues writer, independent of registration. Generated classes
continue receiving the existing concrete store. The user approved starting the first slice and
requested a dedicated branch from the updated primary branch.

Repository inspection found no remote and no main branch; the primary branch is master. Safely
fast-forwarded master to 73f009c and created feature/value-synchronization from that exact commit.
The original feature/server-registration branch is retained at the same commit. No work was lost.
Management value edits remain a separate feature.

### Synchronization slice 1: environment revision - implemented, verified and approved

Added ProjectEnvironment.ValuesRevision and checked advancement, with a database nonnegative
constraint. AddValuesRevision migration initializes empty environments at zero and backfills
populated ones to one without changing definitions. The fixture can migrate isolated databases
to a requested previous migration so upgrade behavior is exercised on the actual old schema.

Registration advances the revision only when inserting missing definitions, once for the complete
batch, in the existing environment-locked transaction and SaveChanges. Reload tracked environment
state under the lock so an already-tracked entity cannot replace a newer persisted revision.
No-op/replayed/conflicting batches do not advance it. Overflow fails instead of wrapping.

Nine focused PostgreSQL checks passed: migration/backfill/reapplication, new/negative revisions,
HTTP insertion/replay/conflict/scope, real constraint failure with rollback and recovery, stale
tracked state, overflow, and simultaneous identical/incompatible/overlapping registrations.
The first full run exposed idle connection pools accumulating across isolated test databases and
hitting PostgreSQL's client limit (two tests failed with 53300). Disabled pooling only for disposable
test-database connections; production settings are unchanged. The subsequent full solution run
passed all 313 tests (117 generator/integration, 97 SDK runtime, 99 server integration), with warnings
treated as errors and no skips, including the packaged consumer. No snapshot query, endpoint or SDK
synchronization yet at that checkpoint. Approved by the user's instruction to move on; remains
uncommitted because that instruction did not request a commit.

### Synchronization slice 2: snapshot query - implemented, verified and approved

Added Features/GetFeatureValues with the agreed query, handler, Response and FeatureValue. The
TinyDispatcher query accepts EnvironmentId and an optional KnownRevision. The handler validates
input and reads the revision and persisted registration defaults in one Repeatable Read transaction.
An equal revision returns an explicit unchanged response without reading the values table; other
revisions return a complete immutable response, including an empty initial revision-zero snapshot.
FeatureValue owns mapping to bool/string values; results use ordinal key ordering in .NET, including
supplementary Unicode keys. Missing environments and database errors propagate rather than becoming
empty successful snapshots. No HTTP endpoint, permission changes, new migration or SDK work.

Tests reuse TinyFlagsServerFactory's actual dispatcher/database configuration. An initial duplicate
test bootstrap was caught by TinyDispatcher's DISP112 diagnostic and removed in favor of that reuse.
Eight focused cases cover typed serialization, stored-default preservation, ordinal order and scope,
empty versus unchanged, older/newer client revisions, invalid/missing environment and cancellation.
The concurrency test uses real table/row locks: pause the values read after it reads the revision,
commit a writer's new flag plus revision, then verify the pending reader still returns the old
consistent snapshot and a subsequent read sees the new one. An unchanged read completes while the
values table is locked, proving it skips that table. No mocks or production test hooks.

After the bootstrap fix, the focused eight cases passed with warnings treated as errors. Full
solution verification then passed all 321 tests (117 generator/integration, 97 SDK runtime,
107 server integration), with no skips, including packaged registration and the new concurrency
test. Explicitly approved by the user. Slices 1 and 2 remain uncommitted. Next is slice 3:
read permission and the authenticated conditional GET endpoint.

### Synchronization slice 3: read permission and HTTP - implemented, verified and approved

Added independent CanReadValues key grants, values:read claims and the ClientValues policy.
AddClientValuesPermission grants existing keys read access; issuance defaults to read access and
supports explicitly denying it, without changing registration grants or stored credential hashes.
GET /v1/client/values always authenticates and authorizes before conditional handling, derives its
environment from the key, and returns typed snapshots or bodyless 304 responses with scoped weak
ETags and no-store. Malformed conditional headers return 400. Query conditions now accept a list
of known revisions or any revision so HTTP lists/wildcards stay within the consistent read transaction.

The user identified excessive protocol logic in Endpoint and approved extracting two concrete types:
Request interprets conditional headers and creates the query; FeatureValuesETag parses and formats
the environment/revision tag, preserving opaque comparison rules. Endpoint only handles the HTTP
flow. Both types belong to this vertical slice; no interfaces, extra layers or DI registrations.

Added HTTP coverage for typed/empty snapshots, changed and unchanged tags, weak/strong/list/wildcard
conditions, environment isolation, invalid/opaque tags, independent grants, revoked/missing keys,
cancellation, database failures and migration of existing keys. Policy tests reject dashboard
identities attempting to supply read permission. All 50 focused tests passed before extraction.
The refactor's first run caught absent-header handling and a nullable warning; both were corrected.
An overlapping rerun encountered a testhost file lock; it ended before starting full verification.
Full solution verification passed all 347 tests (117 generator/integration, 97 SDK runtime,
133 server integration), with warnings treated as errors and no skips, including the packaged
consumer. git diff --check passed. Slice 3 is not yet approved; slices 1-3 remain uncommitted.
SDK fetching and polling remain for later slices.

Review refinement: simplified FeatureValuesETag.TryParse at the user's request. Strip the quotes,
split the contents into named environment/revision text, and validate each with a separate guard.
Exact spelling checks and invariant numeric parsing remain. All 32 GetFeatureValues integration
tests passed after this refinement with warnings treated as errors; git diff --check passed.

At the user's request, added FeatureValuesETagTests with 29 pure unit cases for weak-tag formatting,
valid parsing including zero and Int64.MaxValue, malformed quotes/parts, invalid or alternatively
spelled GUIDs, and invalid/noncanonical/overflowing revisions. The class uses no fixtures, HTTP,
database or mocks and lives beside the feature's existing tests in the server test project.
All 61 GetFeatureValues tests passed (29 unit plus 32 integration), with warnings treated as errors.
Production code is unchanged by this test addition. The user's instruction to commit and move on
approves slice 3 and its refinements. Commit slices 1-3 together, then begin the agreed slice 4:
one SDK fetch and complete snapshot validation. The latest full run passed 347 tests; subsequent
focused runs passed all 61 values tests, including 29 new unit cases (376 distinct tests verified).

### Synchronization slice 4: SDK values and API boundary - implemented, verified and approved

The user approved slice 3 and requested a commit and the next slice. Committed reviewed server work
as 8decce6. During slice 4 review, the user requested application results from the API client for
both operations, a dedicated snapshot reader, and constructor injection of that concrete reader.

TinyFlagsApiClient is the adapter. RegisterDefinitionsAsync completes successfully or reports a
classified TinyFlagsClientException. GetValuesAsync accepts the catalog and optional accepted
snapshot, and returns FeatureValuesResult: updated snapshot or unchanged. It translates conditional
responses, preserves equal/older revisions, and rejects mismatched environments or an unchanged
response without a matching accepted snapshot. Callers do not handle HTTP responses or ETags.

FeatureSnapshotReader owns bounded body reads and JSON/ETag validation. DI injects its concrete
instance into the client; it holds only the configured size limit, with catalog/response state local
to each call. FeatureSnapshot holds immutable typed data and internal conditional-request metadata.
The reader rejects invalid revisions, duplicate properties/keys, blank keys, unsupported/mismatched
kinds and inconsistent tags. Valid unknown keys and absent known keys are allowed. MaxSnapshotBytes
is positive, defaults to 8 MiB, and is checked against declared and actual bytes, including chunked
UTF-8 responses. There is no registration-batch limit on total snapshot size.

The client coordinates the existing TinyFlagsRetryPolicy. Its generic operation supports registration
completion and values results; each attempt includes send and read/translation under one deadline.
A shorter HttpClient timeout applies through body reading too. The policy disposes every response,
retries transient statuses/network/body failures, honors Retry-After and propagates caller cancellation.
Malformed snapshots become a classified InvalidResponse failure, not an immediate transport retry.
No new interfaces, generic result hierarchy, transport adapter layer or Polly dependency was added.

DI owns the API client and its HttpClient. Explicitly supplied HttpClients remain caller-owned.
The registration worker now receives the client and only coordinates startup, catalog registration
and application-level logging. It contains no HTTP status checks, response disposal or retry loop.
Publication and recurring synchronization remain later slices; no live values are updated here.

Full solution verification passed all 447 tests (117 generator/integration, 165 SDK runtime,
165 server tests), with warnings treated as errors and no skips, including the packaged consumer.
Coverage includes reader validation, retry/disposal behavior, real HTTP byte limits, cancellation,
timeouts and interrupted-body recovery, unchanged/stale/environment checks, and real PostgreSQL
registration/read-only access. Approved by the user's instruction to commit and move on. Commit
this slice and then implement slice 5: initial publication through the independent synchronization worker.

### Synchronization slice 5: initial publication - implemented, verified and approved

Committed the approved client/refactor slice as 1aaf214. Added the agreed concrete
TinyFlagsSynchronizationWorker with injected client, singleton FeatureValues, host lifetime and logger.
It waits asynchronously for ApplicationStarted, independently fetches the initial snapshot, checks
cancellation and publishes the complete values. The client continues to own retries and protocol
translation. The worker logs classified failures without stopping the host or changing the store.
No recurring polling is implemented in this slice.

Configured AddTinyFlags registers the synchronization worker once alongside registration; local-only
setup adds neither worker. Existing generated instances and explicitly supplied stores are preserved.
Successful empty snapshots restore code-default fallback. Registration need not complete or succeed
before synchronization. The synchronization worker is the SDK's only FeatureValues writer.

Eleven focused real-HTTP/net8 host tests passed: startup independence, existing-instance updates,
blocked registration, repeated configuration/single publication, empty snapshots, retained warm
values on failures, transient recovery and shutdown before startup/during a streamed body. Two real
API/PostgreSQL cases check read access independently of denied registration and preserve the store
when reading is also denied. The packaged consumer now asserts declared defaults before startup;
after startup an initial snapshot may legitimately replace them, so that old assertion would race.
Full verification passed all 460 tests (117 generator/integration, 176 SDK runtime, 167 server),
with warnings treated as errors and no skips, including the packaged consumer. git diff --check
passed. Approved by the instruction to commit; committed as d325bcf.

Follow-up: replaced the `result.Snapshot is not { } snapshot` pattern match in SynchronizeAsync with
a plain null check per the user's request, matching TinyDispatcher's "boring over impressive" style
guide. 176 SDK tests passed. Committed as f37a736.

### Synchronization slice 6: refresh and recovery - implemented, verified and approved

Added TinyFlagsClientOptions.RefreshInterval (default 30s, validated like RetryDelay/MaxRetryDelay,
included in CreateSnapshot/HasSameConfigurationAs). TinyFlagsSynchronizationWorker now loops after
its first read: PollAsync carries the last accepted FeatureSnapshot forward for each conditional GET,
publishes only on an updated snapshot, and waits RefreshInterval plus 0-10% positive jitter after
each attempt completes, so there is never an overlapping request. Delay is after completion, not a
fixed timer.

Failure handling distinguishes permanent from recoverable per the approved design and the user's
explicit confirmation: CredentialsRejected, AccessDenied, RequestRejected and any unclassified
exception stop the loop for good, same as slice 5's one-shot precedent (a malformed conditional
header or an unanticipated bug will not resolve itself by repeating). InvalidResponse (malformed
JSON body, or a 304 with no accepted snapshot yet) logs and retries on the next normal cycle without
a second retry loop in the worker, matching the design doc's recovery requirement. Transient
network/408/429/5xx failures remain fully handled inside the existing TinyFlagsApiClient/
TinyFlagsRetryPolicy before ever reaching the worker.

Rewrote TinyFlags.Tests/SynchronizationWorkerTests.cs for the recurring loop: replaced waits on a
one-shot ExecuteTask completion with a polling WaitUntilAsync helper for observable state, since the
task now runs for the host's lifetime on success. Split the old single "rejected or invalid" theory
into a permanent-failure case (401/403, loop stops, ExecuteTask completes) and a new
recover-on-next-refresh case (malformed 200 body, 304 without an accepted snapshot). Added tests for
a second poll publishing a newer revision with the correct If-None-Match carried forward, and for
shutdown during the refresh wait stopping promptly with no further request. Added a RefreshInterval
validation theory to TinyFlagsClientOptionsTests.cs. Updated the equivalent real-HTTP/PostgreSQL case
in TinyFlags.Server.IntegrationTests the same way.

While reviewing PollAsync's per-iteration catch, found that an unfiltered `catch (Exception error)`
would also catch OperationCanceledException during shutdown, logging a spurious "synchronization
failed" warning on every graceful stop instead of the silent, expected shutdown the outer ExecuteAsync
handler documents. Added an explicit `catch (OperationCanceledException) { throw; }` before the
generic catch so shutdown still propagates to the existing outer handler unchanged. No test previously
caught this because the observable state (values preserved, task completes) was identical either way;
this is a log-quality/intent fix, not a behavior fix.

Full verification passed all 465 tests (117 generator, 181 SDK runtime, 167 server), with warnings
treated as errors and no skips. Approved by the instruction to commit; committed as e5858fd.

## Completed refinement: retry operation ownership - implemented, verified and approved

The user pointed out that extracting classification alone left retry mechanics in the worker.
Move the full operation into the existing concrete TinyFlagsRetryPolicy: ExecuteAsync accepts
the HTTP call, logger and cancellation token; owns attempts, response/exception classification,
backoff, cancellation and disposal of retry responses; returns the final response to its caller.
The registration worker now waits for startup, submits registration and handles the final result.
No new class/interface/dependency. The existing full suite passed all 305 tests, including the
real HTTP, lost-response and packaged scenarios. After adding two HTTP cases for retry-response
disposal and final-response ownership on success/permanent failure, all 97 SDK tests passed
(307 distinct tests verified across those runs). Warnings were treated as errors. This correction
takes priority over implementing synchronization. Approved by the user's instruction to commit
and move on. The synchronization document remains a proposal for review; no synchronization
implementation has started.

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

Status: slices 1 through 5 approved and committed; slice 5 committed as `0916c4e`.
The single-attempt SDK HTTP client is implemented and verified, awaiting review. No worker yet.

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

### Slice 5: authenticated registration endpoint - implemented and approved

Added Endpoint and Request alongside the existing registration command. POST /v1/client/definitions
requires the client registration policy, derives the environment from authenticated claims and
dispatches through the production TinyDispatcher pipeline. Request bounds are 1 MiB body bytes,
1,000 definitions, 400 key characters and 4,096 string-default characters. Actual stream reads are
bounded even without Content-Length. Unknown envelope members, including environment selectors,
are rejected. Success returns 204; empty batches remain successful no-ops.

Invalid input maps to 400, kind conflicts to 409 with all conflicting keys, excessive body/batch
size to 413 and non-JSON content to 415. Existing authentication/policy provide 401 and 403.
Exception handling runs before authentication because it also accesses PostgreSQL. Transient
Npgsql failures and the EF query/update wrappers map to generic 503; unexpected failures remain
generic 500. Cancellation flows from the HTTP request to dispatch and database waits.

DevelopmentSetup supplies an explicit --seed-development command, restricted to Development.
It creates a local project/environment/key in one SaveChanges, prints the issued secret once,
and exits. It requires migrations to be applied first; ordinary startup does not create data.

Added TinyFlagsServerFactory using WebApplicationFactory and the unchanged production services.
Only environment/connection configuration differs. Host configuration is supplied before Program
reads its connection string; a late application configuration override initially failed startup.
Added Microsoft.AspNetCore.Mvc.Testing 10.0.4. No new database migration in this slice.

Verification: `dotnet test TinyFlags.slnx --no-restore -warnaserror` passed all 217 tests
(117 generator/integration, 21 SDK runtime, 79 server integration), with no skips.
Thirty-six new cases cover the full HTTP pipeline: registration/replay/default preservation,
empty batches, duplicate/case behavior, malformed input, byte/count/value limits (including unknown
body length), 401/403, scope isolation, atomic 409, and two independent hosts with simultaneous
identical/incompatible batches. Cancellation releases blocked database work without writes.
Fault tests stop a private container during authentication, terminate a blocked registration's
database connection, and corrupt only an isolated test schema to verify 503 versus 500 without
leaking internal details. These tests caught and fixed EF's transient-query exception wrapper
initially being classified as 500. The local setup's persisted key is verified through HTTP.

Before committing, the user approved extracting shared test setup. Added the concrete DatabaseSeed
in test Infrastructure: CreateEnvironmentAsync creates a project/environment and returns its ID;
CreateClientAsync creates a project/environment/key together and returns the key plus secret.
Both persist through real DbContext instances in the test's isolated database. Authentication,
handler, concurrency and HTTP tests now share this setup instead of three private seed helpers.
TinyFlagsServerFactory.CreateAuthenticatedClient configures the real HttpClient's Bearer header;
missing-credential tests explicitly use the factory's ordinary CreateClient method.

Scenario-specific setup (revocation, related environments, contention and faults), dispatch helpers,
requests and assertions remain in their tests. Docker ownership/database isolation stays with
PostgreSqlFixture. No production changes or new interfaces from this refactor. Verification after
extraction: the same 217 tests passed with warnings treated as errors, including all 79 server
integration tests; no tests were removed or skipped.

Approved by the user's instruction to commit and move on. Next slice adds one HTTP registration
attempt from the SDK client.

### Slice 6: one SDK HTTP attempt - implemented and approved

Added the agreed public TinyFlagsClientOptions and internal TinyFlagsApiClient. Options currently
contain Endpoint, ApiKey and a 30-second RequestTimeout; retry settings wait until slice 8.
Validate URI/credential/timeout locally and snapshot settings at construction. Require HTTPS
except HTTP loopback development; preserve an optional base path and normalize its trailing slash.
Reject URI credentials, query/fragment and whitespace/control/non-ASCII key characters.

Each call snapshots existing immutable FeatureDefinition entries, serializes string kind names
and typed defaults, and sends a single authenticated POST. Authorization belongs to that request;
HttpClient defaults are not mutated. Caller cancellation and the request timeout are linked.
ResponseContentRead keeps response buffering inside the deadline. Return the actual response,
including error status, headers and body, for caller disposal. No retry or EnsureSuccessStatusCode.
The caller owns HttpClient, whose own timeout still applies if shorter. No new NuGet dependencies,
custom transport interfaces, DI overload or worker. Friend-assembly access lets tests exercise the
internal client without widening its public API; the integration project now references the SDK.

Verification: `dotnet test TinyFlags.slnx --no-restore -warnaserror` passed all 245 tests
(117 generator/integration, 38 SDK runtime, 90 server integration), with no skips.
Seventeen new local validation cases and eleven integration cases cover typed/escaped defaults,
replay, empty batches, configuration snapshots, per-request credentials, permanent error responses,
cancellation and timeout behind a real PostgreSQL lock, recovery on a subsequent caller attempt,
and a real container outage returning 503. A loopback Kestrel server verifies path prefixes with
and without trailing slash, one request per attempt, JSON types, and preserved Retry-After/body
for 429 and 503. No mocked transport or repositories.

`tests/TinyFlags.PackageTests/Verify-Package.ps1` also passed: package structure/dependencies,
separate net8.0 consumer execution and expected TFG002 diagnostics. Artifacts remain under
artifacts/package-tests/db53676f8cbc44cf8548230195f09c70. Packaged HTTP end-to-end remains slice 9.

Approved by the user's instruction to continue with slice 7. Remains uncommitted.

### Slice 7: non-blocking startup - implemented and approved

Added the configured AddTinyFlags overload and internal TinyFlagsRegistrationWorker. Setup validates
and copies options, preserves existing local stores, deduplicates equivalent configuration and
rejects conflicting repeated configuration before changing registrations. The parameterless overload
remains local-only. Hosting.Abstractions 8.0.1 is the additional direct runtime dependency.

The worker waits for ApplicationStarted through a private asynchronously continued completion
source. Only then does it compose the catalog and send one request. It owns and disposes the HTTP
client/response, disables redirects, and links host shutdown to pending waits and I/O. Success,
HTTP failure, timeout and exceptions finish the attempt without changing local values or stopping
the host. Logs omit credentials, response bodies and exception messages. Retries remain slice 8.

Verification: dotnet test TinyFlags.slnx --no-restore -warnaserror passed all 259 tests
(117 generator/integration, 51 SDK runtime, 91 server integration), with no skips.
Thirteen native net8.0 lifecycle/configuration cases use real hosts and loopback Kestrel: serving
generated defaults while registration is blocked, one attempt after repeated configuration,
snapshot isolation, preserved explicit values, invalid/conflicting configuration, cancellation
before startup and during I/O, HTTP failures/redirects, timeout and connection failure.
One full integration case runs the worker against the production Kestrel/auth/dispatcher/PostgreSQL
path: host startup completes while a database row lock blocks registration, and releasing the
lock persists the generated catalog in the authenticated environment.

Package verification also passed with both runtime dependencies, separate net8.0 consumer behavior
and expected TFG002 diagnostics. Artifacts: artifacts/package-tests/1c59fb4c765f482482e22040a0fa42f9.
Packaged HTTP end-to-end remains slice 9.

The user explicitly retained net8.0 for client compatibility with .NET 8 applications that later
upgrade to .NET 10; server remains net10.0 and generator netstandard2.0. With user authorization,
installed .NET SDK 8.0.425 globally, including ASP.NET Core 8.0.31 needed by native host tests.

Approved by the user's instruction to commit and move on. Next slice adds retry classification
and recovery using the agreed concrete TinyFlagsRetryPolicy, without Polly or custom interfaces.

### Slice 8: retry and recovery - implemented and approved

Slices 6 and 7 are committed in 3df0288. The user approved a concrete TinyFlagsRetryPolicy owning
retry classification and delay calculation; the worker retains HTTP execution and cancellation.
Value refresh remains a separate feature, with normal polling distinct from failed-request retries.

Implemented policy: network failures, request timeouts, 408, 429 and 5xx retry; other responses and
unexpected local exceptions stop. The worker snapshots the catalog once, reuses one HttpClient,
awaits each request, disposes its response, and waits before the next attempt. Success is 204.
RetryDelay defaults to one second, MaxRetryDelay to 30 seconds; equal jitter chooses between half
and the full capped exponential ceiling, with a one-millisecond floor. Both options are validated,
copied and checked when configuration repeats. Saturated attempt state prevents overflow.

Valid Retry-After delta/date values on 429/503 impose a minimum delay, including beyond the normal
cap. Malformed/expired headers retain backoff. Long waits are split into cancellable one-day
segments to preserve the server minimum without exceeding Task.Delay's range. Shutdown cancels
requests and waits; transient outages may retry indefinitely. No Polly, custom interface, new
package dependency, value synchronization or additional endpoint was introduced.

Verification: dotnet test TinyFlags.slnx --no-restore -warnaserror passed 304 tests
(117 generator/integration, 95 SDK runtime, 92 server integration), with no skips.
Coverage includes status classification, growing/jittered/capped backoff, long and malformed
Retry-After, configuration validation, HTTP recovery with the same catalog, permanent failures,
connection abort/timeout recovery, observed server minimum delay and shutdown after a retryable
response with long backoff. Existing startup/default/shutdown behavior remains covered.
A loopback proxy forwards to the actual Kestrel/auth/dispatcher/PostgreSQL API, drops the first
response after commit, and verifies the worker retries successfully with no duplicate rows or
changed creation timestamps. Package wiring/dependencies are unchanged; packaged HTTP verification
remains the next approved feature-plan slice (9).

Approved by the user's instruction to keep going and included in the requested commit with slice 9.

### Slice 9: packaged end to end - implemented, verified and approved

Reuse the existing package verification script, real PostgreSqlFixture, DatabaseSeed and
TinyFlagsServerFactory. The new integration test packs a unique local NuGet version, restores
a separate net8.0 host/library pair into an isolated package cache, checks local behavior and
TFG002 diagnostics, then runs that built consumer as a separate process against Kestrel.

The consumer's --register mode uses public AddTinyFlags configuration and generated access classes.
It reports startup only after resolving defaults from both assemblies, then accepts an explicit
stdin shutdown signal. Test-only endpoint/key configuration uses child environment variables,
not process arguments. The package host uses the ASP.NET Core 8 shared framework for hosting.
The PowerShell script accepts an optional run directory so build and process logs stay together.

The test holds the environment row lock, proves consumer startup completes while registration
is blocked, releases the lock and verifies all three definitions in the authenticated environment.
It then rebuilds with changed boolean/string defaults plus a new flag, launches a second process,
and verifies the original defaults/timestamps remain unchanged while the new definition is added.
Only the packed TinyFlags dependency supplies runtime and analyzer assets to these consumers.
No production changes or new test abstractions were needed for this slice.

Focused packaged test passed, including package contents/dependencies, local consumer execution,
expected TFG002, real HTTP registration and redeployment. Full solution verification then passed
all 305 tests (117 generator/integration, 95 SDK runtime, 93 server integration), with warnings
treated as errors and no skipped tests. The packaged scenario also passed within that full run.
Build/consumer logs remain under artifacts/package-tests/cda9f864775b4dd091cf0fd42370a795.

Approved by the user's instruction to commit and move on. Registration feature work is complete.
Versioned value retrieval and synchronization need their own design and agreed slices before
implementation.

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

## Open problem: admin auth has no scope (not yet slice-planned)

Raised 2026-09-21, end of the design-pass/samples/docs-style session. Not started; this is intent
for a future feature, not an agreed slice breakdown yet.

**The problem.** `AdminApiKey` has no scope. Every admin token reaches every project on the
server — there is no way to hand someone access to just their own team's flags. The user called
this a real code smell, not a nitpick, and specifically for a self-hosted product: TinyFlags.Server
already supports multiple projects/environments in one database, but the admin trust model is
flat, so the data model and the trust model disagree with each other.

**First proposal and why it was wrong.** Scope `AdminApiKey` to a `ProjectId`, issued via a new
`--issue-admin-key --project <guid>` CLI flag. The user rejected this, correctly: as described, it
still required shell/`docker exec` access to grant a team lead their own scoped key, which is not
an acceptable normal workflow for a self-hosted admin tool. Requiring a terminal for *every*
delegation, not just the very first bootstrap key, was the actual smell — scoping the blast radius
without fixing how you get a scoped key doesn't solve the usability problem.

**Corrected direction, agreed in discussion, not yet slice-planned.**

- `AdminApiKey` gets a nullable `ProjectId`. Null = global admin (today's behavior, unchanged).
  Non-null = scoped to that one project's environments, flags, and client keys.
- `--issue-admin-key` keeps its CLI form *only* for the very first admin key on a fresh install —
  the unavoidable bootstrap case shared by every self-hosted tool (Grafana, Gitea, etc. all have
  some form of this: no admin exists yet, so nothing but shell access can create one).
- Every key after that — including a team lead's project-scoped key — must be issuable from
  `/Admin/Tokens` in the browser, not the CLI. That page already lets an existing admin issue more
  admin tokens; it needs a project-scope picker added to its issue form, the same shape
  `/Admin/Keys` already uses to pick an environment for a client key.
- Every dashboard query scopes by the calling key's `ProjectId` when it is non-null. A
  project-scoped key can issue further keys, but never scoped broader than its own — it can't
  mint itself a global key.
- Deliberately still not RBAC: one scope dimension (project), not permission tiers
  (read-only/read-write admin), which the user already ruled out earlier as unnecessary weight for
  what this product needs. Still a hashed bearer secret, still no passwords, no user accounts, no
  ASP.NET Core Identity — additive to the existing model, not a replacement.

**Login UX for a scoped key — unchanged.** `/Admin/Login` stays exactly as it is today: one
"Admin token" field, no project picker. The token itself already encodes the scope, so there's
nothing extra to ask at login. What changes is invisible at login time: on success, the auth
cookie gets a `ProjectId` claim baked into the ticket, mirroring how
`ClientApiKeyAuthenticationHandler` already puts `EnvironmentClaim`/`ProjectClaim` on *client* key
tickets — admin tickets carry no scope claim today, so this is the same pattern applied there too.
Every subsequent page enforces the claim server-side (query filtering), not just in the UI — a
crafted URL pointing at another project's environment returns nothing, it isn't merely hidden.

**Known remaining gap, explicitly out of scope for this fix.** This still identifies a *key*, not
a *person* — no per-person identity, no real "who clicked this" audit trail, just which token was
used. That is the same territory as the earlier OIDC/SaaS conversation and is a separate, larger
conversation, not something to fold into this fix.

**Next session:** turn the corrected direction above into an actual slice breakdown (data model
change, CLI bootstrap-only restriction, `/Admin/Tokens` UI change, query scoping) before writing
any code, per the working agreement.

**Token lifecycle and loss recovery — already solved by existing behavior, no new design needed.**
The user asked whether the admin keeps the bootstrap token forever, requests a new one, or what
happens if it's lost. Answer, using what already exists today:

- The bootstrap token isn't meant to be permanent. Right after minting it via shell, log in once
  and use `/Admin/Tokens` to issue a second, properly-labeled personal key, store that in a
  password manager, and optionally revoke the raw bootstrap one.
- Losing *a* token, while at least one other still works: log in with the working one, revoke the
  lost one, issue a new one. Already supported — `/Admin/Tokens` allows multiple coexisting admin
  keys, each independently revocable, exactly for this.
- Losing *every* token at once: back to shell/`docker exec` + `--issue-admin-key`, same as day 1.
  Not a gap to design around — whoever has infra access to the container can always regain
  control; if they don't, that's an infra-permissions problem, not a TinyFlags one.

Nothing to build here. Worth a line in `docs/admin-dashboard.md` about the "mint yourself a
personal token, don't rely on the bootstrap one" practice when the scoping slice above is done.

Note (2026-09-21, later the same session): `TinyFlags.Server` has since moved to its own private
repo. The admin-auth-scoping and licensing sections above now describe work that lives there, not
here — carried forward verbatim into that repo's own `PLAN.md`. Left in place here too, as
historical record of when and why the decision was made; not re-litigated or removed.

## Resolved: repo history before going public

Superseded same day (2026-09-21). Rewriting `main`'s history alone wouldn't have hidden the old
merged PRs' server-source diffs (GitHub keeps `refs/pull/N/head` forever; PRs can't be deleted
self-service). Went with the airtight option instead: deleted the old `TinyFlags` repo entirely
and recreated it fresh under the same name, then pushed the already-rewritten, server-free `main`
(65 commits) as its entire history. No PRs, no issues, no stale refs — nothing to find. Still
private; public flip remains a future decision, now unblocked by any history concern.

## Next feature (draft, not yet approved): pluggable server transport

Raised 2026-09-21, drafted 2026-09-22. Goal: let people build their own server (not just
`TinyFlags.Server`) without depending on its exact HTTP surface, by exposing public contracts for
the SDK's independent responsibilities, and keep the HTTP implementation as one implementation of
those contracts, not a privileged special case. This is a draft to mark up, not an approved design
— nothing here is implemented, and per `WORKING-AGREEMENT.md` the 4-point abstraction ritual still
needs to run per contract before any code.

### Current shape, as it exists in code today

`TinyFlagsRegistrationWorker` and `TinyFlagsSynchronizationWorker` are `BackgroundService`s that
already only depend on `TinyFlagsApiClient` through two calls — the seam is basically already
there, just concrete and HTTP-only:

```csharp
Task RegisterDefinitionsAsync(IReadOnlyList<FeatureDefinition>, CancellationToken)
Task<FeatureValuesResult> GetValuesAsync(IReadOnlyList<FeatureDefinition>, FeatureSnapshot? current, CancellationToken)
```

Two findings from reading the actual code, independent of whether the bigger split happens:

- `FeatureSnapshot.EntityTag` carries zero information beyond `EnvironmentId` + `Revision` —
  `FeatureSnapshotReader.ReadEntityTag` synthesizes and validates it as exactly
  `"{environmentId}:{revision}"`. The transport-agnostic cursor only needs to be
  `(EnvironmentId, Revision)`; HTTP can rebuild its own `If-None-Match` from those two without a
  separate field crossing the boundary.
- `TinyFlagsRetryPolicy` classifies HTTP status codes and honors `Retry-After` — this is HTTP-only
  and must stay inside the HTTP implementation. Other transports (Redis, gRPC) have their own
  retry/deadline shape; the contract must not assume HTTP failure semantics.

### Proposed contracts (three, not two — registration needs its own)

Registration is inherently one-shot request/response regardless of transport — no pull/push
duality applies to it, unlike values:

```csharp
public interface IFeatureDefinitionsTransport
{
    Task RegisterAsync(IReadOnlyList<FeatureDefinition> definitions, CancellationToken ct);
}
```

Values has the pull/push fork discussed 2026-09-21 (polling vs. gRPC-stream/Redis-pub-sub/etcd-watch
style real-time updates). Two dedicated workers per the user's direction — a polling worker driving
`IFeatureValuesTransport`'s loop/jitter/backoff itself, and a separate, simpler worker that just
drains `IFeatureValuesSubscription`'s stream:

```csharp
public interface IFeatureValuesTransport
{
    Task<FeatureValuesResult> GetValuesAsync(IReadOnlyList<FeatureDefinition> catalog,
        FeatureValuesCursor? current, CancellationToken ct);
}

public interface IFeatureValuesSubscription
{
    IAsyncEnumerable<FeatureValuesResult> WatchAsync(IReadOnlyList<FeatureDefinition> catalog,
        CancellationToken ct);
}
```

`FeatureValuesCursor` would replace today's internal `FeatureSnapshot` as the public cursor type —
just `EnvironmentId` + `Revision`, no `EntityTag`.

### Open questions for the user to mark up

1. **Packaging — revised 2026-09-22, now shipped.** Originally decided to keep HTTP in core
   (matching `TinyEvents`: separate packages only for implementations with a heavy external
   dependency, and `HttpClient` needs none). Superseded same day by a different, equally valid
   rationale once the actual goal was named explicitly: HTTP is meant to be *the worked example* of
   how to build a transport, symmetric with a future `TinyFlags.Grpc` sample (HTTP → the pull
   interfaces, gRPC → push/`IFeatureValuesSubscription`, real-time). Leaving HTTP specially wired
   into core while gRPC lived outside would make HTTP look privileged instead of like the first
   example. Extracted into `TinyFlags.Http` — implemented, verified, committed.

   What actually moved: `TinyFlagsApiClient`, `TinyFlagsRetryPolicy`, `FeatureSnapshotReader`,
   `FeatureSnapshotEntityTag`, and `TinyFlagsClientOptions` (renamed `TinyFlagsHttpOptions` — it's
   HTTP-shaped, a gRPC channel has none of `Endpoint`/`ApiKey`/retry settings). What stayed in core,
   confirmed necessary while splitting: the three transport contracts and cursor/result types (as
   before); the two workers (`TinyFlagsRegistrationWorker`, `TinyFlagsSynchronizationWorker`) since
   they're pure logic against an interface, not HTTP-specific; `TinyFlagsClientException`/
   `TinyFlagsClientFailure`, which had to go from `internal` to public once thrown from one
   assembly and caught in another — `InternalsVisibleTo` would only work for packages we
   explicitly allowlist, defeating "let anyone build a transport." Same reasoning forced the two
   workers from `internal` to `public` — `UseHttpTransport` needs to call `AddHostedService<T>()`
   on them from a different assembly.

   One real design fork resolved along the way: `TinyFlagsSynchronizationWorker` only used
   `RefreshInterval` from the HTTP options — genuinely transport-agnostic (every pull transport
   needs a poll interval; push transports need none), so it couldn't stay coupled to HTTP-only
   options once split. Extracted to `FeatureValuesPollingOptions` in core; `UseHttpTransport`
   still exposes `RefreshInterval` as a convenience property on `TinyFlagsHttpOptions` and
   translates it internally, so the configuration experience is unchanged even though the actual
   dependency moved.

   Revised again same day: the first cut had samples calling `UseHttpTransport(...)` alone, with
   `AddTinyFlags` never appearing anywhere in consumer code. Called out as a real problem, not
   style — `AddTinyFlags` is the framework's recognizable root entry point (same role as
   `AddMediatR`), and a public library where different consumers pick different transports needs
   that root always visible, unlike a single-owner app that only ever uses one backend (checked
   real `TinyEvents.Dogfood` code: the single-consumer Worker app calls `UseTinyEvents(...)`
   explicitly because it has real settings to configure; the Producer app, with nothing to
   configure, calls only `UseSqlServerAdoNetOutbox(...)` — neither example actually argues for
   "always call the root," they just don't apply to a multi-transport public library). Fixed by
   having `AddTinyFlags` take `Action<TinyFlagsOptions>? configure`, where `TinyFlagsOptions`
   exposes `Services` for a transport package's own extension method to register against —
   `TinyFlags.Http`'s `UseHttpTransport` is now `TinyFlagsOptions -> TinyFlagsOptions`, not
   `IServiceCollection -> IServiceCollection`. Usage:
   `services.AddTinyFlags(tinyFlags => tinyFlags.UseHttpTransport(options => ...))` — one
   recognizable root regardless of transport, core never references HTTP. All 15 call sites across
   the HTTP test project and both samples updated to the nested shape; full suite re-verified green
   (298 tests) and re-checked live against the published server once more.

   New project layout: `src/TinyFlags.Http` (implementation), `tests/TinyFlags.Http.Tests`
   (mirroring the `TinyEvents.PostgreSql.AdoNet.Tests`-style per-package split — confirmed as
   precedent before doing it). `tests/TinyFlags.Tests` now only holds transport-agnostic tests
   (`FeatureDefinitionTests`, `FeatureValuesTests`); everything HTTP-shaped moved with the code.
   All 298 tests still pass, split 21/117/160 across the three test projects, zero lost or
   duplicated. Samples (`OrdersService`, `PaymentsService`) updated: new `ProjectReference`, their
   Dockerfiles needed an added `COPY src/TinyFlags.Http/` line (caught by a real Docker build
   failure, not guessed), `AddTinyFlags(configure)` calls became `UseHttpTransport(configure)`.
   Re-verified live against the published `TinyFlags.Server` image after the split, same healthy
   signature as every other slice tonight. `README.md`/`docs/getting-started.md` code samples
   fixed since they'd otherwise not compile (`AddTinyFlags(options => ...)` no longer exists) —
   the fuller docs pass explaining the transport model itself is still slice 6, not done here.
2. **API surface consequence** — `FeatureSnapshot`/`FeatureValuesResult` are `internal` today.
   Once an external package implements these interfaces and returns these types, they're
   permanent public API with real naming/versioning stakes, not private plumbing anymore.
3. **Registration contract — resolved 2026-09-22.** One `IFeatureDefinitionsTransport` is enough.
   `TinyFlagsRegistrationWorker` calls it exactly once at boot, no loop — nothing to pull (the
   catalog is already known locally) and nothing to push (nothing external decides when
   registration happens). `RegisterAsync` stays `Task`, no revision in or out: today's HTTP
   contract already returns `204, no body` on success, and revision tracking belongs entirely to
   the values side by design (`architecture.md`: "registration never mutates `FeatureValues`, and
   synchronization never waits for registration to complete or succeed"). Checked and ruled out:
   having `RegisterAsync` return a revision to save a round trip on first sync — no gain, since
   the values worker's first call already passes `current: null` and fetches unconditionally
   regardless.
4. Exact interface/type names above are placeholders, not proposals to lock in.

### Slice breakdown (proposed 2026-09-22, none started)

Per `WORKING-AGREEMENT.md`: each slice still gets its own goal/existing-code/proposed-change
presentation and explicit approval immediately before it's implemented. This is the ordering, not
a green light to start coding.

1. **Cursor cleanup — implemented and verified 2026-09-22, awaiting review.** Removed
   `FeatureSnapshot.EntityTag`; added `FeatureSnapshotEntityTag.Format(Guid, long)` as the single
   place that derives the tag content from `EnvironmentId`+`Revision`, used by both
   `FeatureSnapshotReader`'s validation and the outgoing `If-None-Match` header.
   `FeatureValuesCursor` was **not** introduced here after all — it would sit unused until slice 3
   actually consumes it in a public interface signature, which is premature per the "abstractions
   earn their place" rule; deferred to slice 3.

   Real finding, not just mechanical: `docs/protocol.md` fixes `If-None-Match` as a **weak** entity
   tag (`W/"..."`) — part of the actual wire contract, not an implementation detail. The first cut
   of the shared helper dropped the `W/` prefix, which would have made the SDK send a strong tag
   and quietly violate our own documented protocol against the real server. A test asserting the
   exact outgoing header caught it at build time (compile error led to checking the docs, not the
   test failing silently) before it could ship. Fixed: the reader's validation compares tag
   *content* only (the framework already strips `W/` there), the outgoing request always adds
   `W/` explicitly and unconditionally, matching the documented format rather than echoing
   whatever was last received. Full suite green: 117 + 181 = 298 passed, 0 warnings.
2. **Registration transport — implemented and verified 2026-09-22, awaiting review.** Added
   `IFeatureDefinitionsTransport` under `Abstractions/` (one method, `RegisterAsync`).
   `TinyFlagsApiClient` implements it (`RegisterDefinitionsAsync` renamed to `RegisterAsync` to
   match). `TinyFlagsRegistrationWorker` now depends on the interface, not the concrete class. DI
   keeps the one `TinyFlagsApiClient` singleton (still needed by the untouched values side) and
   additionally exposes it as `IFeatureDefinitionsTransport`. Full suite green: 117 + 181 = 298
   passed, 0 warnings — including `RegistrationWorkerTests.cs`, which already exercises the new
   wiring through real `AddTinyFlags` → DI → Kestrel, not mocks.
3. **Values pull transport — implemented and verified 2026-09-22, awaiting review.** Decided
   between two shapes for the newly-public API (discussed live): keep `FeatureSnapshot` as one
   type bundling identity and payload, or decompose it. Went with decomposing (option B) —
   `FeatureSnapshot` is gone entirely, replaced by two public types in `Abstractions/`:
   `FeatureValuesCursor` (`EnvironmentId`+`Revision` only — identity/staleness, flows in as
   `current` and back out as `FeatureValuesResult.Cursor`) and `FeatureValuesResult`
   (`Cursor`+`Values`, `IsUnchanged` when `Cursor` is null). This removed the redundant
   environment/revision reconciliation the old code did between two overlapping objects on every
   call. `FeatureSnapshotReader.ReadAsync` now returns an internal `(FeatureValuesCursor, Dictionary)`
   tuple rather than constructing a type of its own.

   Caught while fixing tests: the old `FeatureSnapshot.Values` was defensively wrapped in
   `ReadOnlyDictionary`, a tested guarantee (mutation threw `NotSupportedException`). The first cut
   of `FeatureValuesResult.Updated(...)` just took the dictionary by its read-only interface type
   without actually enforcing immutability — same class of near-miss as slice 1's ETag prefix, an
   implicit guarantee almost dropped silently while moving code. Fixed: `Updated(...)` now always
   copies into a fresh `ReadOnlyDictionary` at construction, so the guarantee holds regardless of
   what any transport (ours or a future one) passes in. Test restored and re-verified.

   Full suite green (117 + 181 = 298, 0 warnings) and re-verified live against the published
   `TinyFlags.Server` image via `samples/MultiService` — registered and synchronized 0 → 2 cleanly,
   no warnings in any container.
4. **Values push transport + second worker — implemented and verified 2026-09-22, awaiting review.**
   Decided: reuse `FeatureValuesResult` for the stream (option 1), and the name
   `TinyFlagsValuesWatchWorker` (option 2) — both confirmed by the user. `TinyFlags.Grpc` is the
   planned real consumer (see `TinyFlags.Server`'s `PLAN.md` for the cross-repo gRPC plan).

   `IFeatureValuesSubscription` added under `Abstractions/`, one method,
   `WatchAsync(catalog, ct) -> IAsyncEnumerable<FeatureValuesResult>`. `TinyFlagsValuesWatchWorker`
   added under `Synchronization/`, `public` from the start this time (learned that lesson from
   `TinyFlagsRegistrationWorker`/`TinyFlagsSynchronizationWorker` needing it discovered mid-slice
   last time) — mirrors the pull worker's startup/cancellation handling with no poll loop at all,
   just `await foreach`, skipping `IsUnchanged` results defensively and replacing `FeatureValues`
   on real updates. Each transport's `Use...` extension stays responsible for adding only the
   workers its own interfaces satisfy, same pattern `UseHttpTransport` already uses — a push-only
   setup must not add the pull worker and vice versa.

   No concrete implementation exists yet (that's `TinyFlags.Grpc`, not started), so tested against
   a hand-built fake `IFeatureValuesSubscription` — the first core test in this codebase to use a
   test double rather than a real collaborator, and correctly so: there is no real transport yet
   to test against, and the point of these four tests is the *worker's* contract with any
   transport, not any one transport's behavior. Covers: applies updates in order and skips
   `Unchanged`, host shutdown cancels an in-flight watch cleanly, a `TinyFlagsClientException`
   stops the worker without crashing the host, an unexpected exception does the same. All pass;
   full suite green at 302 (25 core / 117 source-gen / 160 HTTP).
5. **DI ergonomics — done, ahead of schedule.** Turned out to be needed immediately, not later —
   see "Packaging" above (revised 2026-09-22): `AddTinyFlags(Action<TinyFlagsOptions>?)` is the one
   entry point, `TinyFlagsOptions.Services` is what a transport's own extension method registers
   against. Already covers this slice's original question.
6. **Docs.** Update `architecture.md`/`registration.md`/`value-synchronization.md`/`protocol.md`
   to describe the contracts, mark `TinyFlags.Server` explicitly as "the reference HTTP
   implementation," and add a "build your own transport" guide — the actual deliverable for the
   "we let clever guys do that" goal, since we're shipping seams and docs, not other transports.
7. **`TinyFlags.Grpc` — implemented, unit-tested, awaiting live verification.** Implements
   **both** `IFeatureDefinitionsTransport` and `IFeatureValuesSubscription` (revised 2026-09-22 —
   gRPC must work with no HTTP transport running at all), mirroring `TinyFlags.Http`'s shape:
   `TinyFlagsGrpcOptions` (`Endpoint`, `ApiKey`, `ReconnectDelay`), `tinyFlags.UseGrpcTransport(...)`
   extending `TinyFlagsOptions`, registering `TinyFlagsRegistrationWorker` (shared, unchanged) and
   `TinyFlagsValuesWatchWorker` (the push worker, not the polling one).

   `TinyFlagsGrpcTransport` implements both interfaces against one `GrpcChannel`. `RegisterAsync`
   is a single call, `RpcException` status codes mapped to `TinyFlagsClientFailure` the same way
   `TinyFlagsApiClient` maps HTTP statuses (`Unauthenticated`→`CredentialsRejected`,
   `PermissionDenied`→`AccessDenied`, `AlreadyExists`→`DefinitionsConflict`, etc.). `WatchAsync`
   reconnects on transient status codes (`Unavailable`/`DeadlineExceeded`/`Internal`/`Aborted`)
   after `ReconnectDelay`, but lets permanent failures (bad credentials) propagate as
   `TinyFlagsClientException` so `TinyFlagsValuesWatchWorker` correctly stops instead of retrying
   forever — reconnection is explicitly this transport's job per the `.proto`'s own contract, not
   the worker's. `TinyFlagsGrpcOptions.Validate()` mirrors `TinyFlagsHttpOptions`'s exact endpoint
   rule (HTTPS, or HTTP on loopback only) for the same reason: the Bearer token travels as call
   metadata, exactly as leakable over plain HTTP to a remote host as an `Authorization` header —
   caught by a test expecting `file:///flags` to be rejected and finding it wasn't, since
   `IsAbsoluteUri` alone doesn't reject non-HTTP schemes.

   Unit-tested (options validation/equality, 12 tests, all green) — full suite 314 (25 core + 12
   gRPC + 117 source-gen + 160 HTTP), one unrelated pre-existing timing flake in
   `TinyFlags.Http.Tests` confirmed by rerunning it alone.

   **Live verification — done, no Docker, no publishing.** Ran `TinyFlags.Server` directly via
   `dotnet run` against a throwaway local Postgres container (not `WebApplicationFactory`, not a
   published image — that step is still explicitly deferred until the server side is reviewed and
   released for real, per the user's own call). A real `TinyFlagsGrpcTransport` registered a
   definition and received it pushed back over a real `Watch` stream after a second registration.

   This caught two real bugs neither side's unit/in-memory tests could have: Kestrel's h2c
   negotiation needing two separate endpoints, not one shared one (see `TinyFlags.Server`'s
   `PLAN.md`), and `Grpc.Net.Client` silently refusing to even attempt HTTP/2 over plain `http://`
   without `Http2UnencryptedSupport` explicitly enabled — fixed in `TinyFlagsGrpcTransport`'s
   constructor, scoped to only the loopback-`http` case `Validate()` already allows. Both sides
   confirmed clean afterward: 314 tests here (25 core + 12 gRPC + 117 source-gen + 160 HTTP), 200
   on the server side.

   **Still explicitly deferred, on purpose:** the permanent two-sample setup (HTTP unchanged, a
   new gRPC sample pulling a published image) needs `TinyFlags.Server`'s gRPC branch reviewed,
   merged, and released as a real versioned image first — a sample built against unpublished
   private-repo source would break for anyone who isn't the owner the moment they clone the public
   client repo. Not something to build ahead of that decision.

   Wire contract: `src/TinyFlags.Grpc/Protos/tinyflags.proto`, two independent services, neither
   depending on the other existing — `TinyFlagsDefinitions.Register` (unary, one-shot) and
   `TinyFlagsValues.Watch` (streaming `ValuesSnapshot`, `environment_id` cross-checked on every
   message the same way `TinyFlagsApiClient` cross-checks its ETag). Full cross-repo plan is in
   `TinyFlags.Server`'s own `PLAN.md`, including the server-side `LISTEN`/`NOTIFY` mechanism and
   both gRPC service implementations — already built, tested, and live-verified there via a real
   `GrpcChannel` against `WebApplicationFactory`'s `TestServer`.

8. **Unblocked 2026-09-22: `TinyFlags.Server`'s gRPC branch merged (PR #2) and released as
   `ghcr.io/george2006/tinyflags-server:0.2.0`/`:latest`.** CI on that repo had been failing on two
   sibling-repo filesystem paths only present on the dev machine (`TinyEvents` via `ProjectReference`,
   `tinyflags.proto` via a relative path into this repo) — fixed by switching `TinyEvents` to its
   real published NuGet packages (`1.0.0-beta.1`, already on nuget.org) and vendoring a copy of
   `tinyflags.proto` into `TinyFlags.Server` itself, since that repo is and stays private and can't
   be reached from CI or from a Docker build context either way. Verified before merge: clean build,
   200/200 tests (44 unit + 156 integration, real Testcontainers), and a real
   `docker build -f src/TinyFlags.Server/Dockerfile src/TinyFlags.Server` from the scoped context.
   Tagged `v0.2.0`, `Release` workflow pushed the image to GHCR — the blocker above is cleared.

   Revised sample scope, decided in discussion the same day: not the two-sample setup this section
   originally described. Three samples instead, one folder/solution/README each, replacing
   `samples/MultiService`:
   - `samples/MultiServiceHttp` — `samples/MultiService`, renamed only (content unchanged): two
     services sharing one flag over HTTP, the existing multi-service story.
   - `samples/SingleServiceHttp` — new, one minimal service, `UseHttpTransport`, stripped of the
     cross-service flag-sharing concern MultiService exists to demonstrate.
   - `samples/SingleServiceGrpc` — new, same minimal shape as the above but `UseGrpcTransport`,
     the first sample proving the push/`IFeatureValuesSubscription` path end-to-end against a
     released (not locally-built) server image.

   Rationale: MultiService was teaching two things at once (cross-service flag sharing, and "how do
   I wire transport X") — splitting means someone who only wants "how do I wire gRPC" doesn't have
   to parse `OrdersService`/`PaymentsService`/`SharedPromotions.cs` to find it.

   **Done 2026-09-22, three reviewable slices, each approved and verified live before the next:**
   1. Renamed `samples/MultiService` → `samples/MultiServiceHttp`, content unchanged, every doc
      link fixed.
   2. Found and fixed a real regression while verifying slice 1 live, not a rename artifact:
      `TinyFlags.Server` v0.2.0 now always binds `8081` for gRPC, which collided with
      `orders-service`'s own port inside the shared compose network namespace (`address already in
      use`, crash loop). Moved it to `8083`.
   3. Added `samples/SingleServiceHttp` and `samples/SingleServiceGrpc` — one flag
      (`MyApp.Checkout.NuevoCheckout`/`TextoBoton`, the exact declaration from
      `getting-started.md`'s Option B/C) each, own folder/`.slnx`/`README.md`. Both verified live:
      real `docker compose up` against the released image, real HTTP requests. The gRPC sample's
      push path was proven, not assumed — issued a real write-capable client key
      (`--issue-key --values write`), `PATCH`ed a value through the actual `/v1/client/values`
      endpoint, and confirmed the running app's `/flags` reflected it on the very next request,
      pushed over the open `Watch` stream rather than fetched by a poll.

   `README.md` and `docs/getting-started.md` updated to link all three samples and drop the
   now-stale "gRPC sample is planned" line — gRPC shipped this session.

## Today (2026-09-23): pre-public consolidation pass — not yet started

Yesterday shipped the feature (gRPC transport, `TinyFlags.Server` released, three samples). Today
is deliberately not a feature day: the user set the goal of going public with this repo by the end
of the week, and wants a consolidation pass first, reviewing every file under `src/` (~45 files,
excluding generated `obj/`, across the four projects) as if handing it to another principal
engineer to read cold. Framed by the user as two sequential steps, each broken into small,
reviewable slices per `WORKING-AGREEMENT.md` rather than one uninterrupted sweep:

**Step 1 — documentation.** Every file gets read for whether a newcomer could follow it: accurate
XML doc comments on public API, inline comments that explain *why* (a constraint, a tradeoff, a
non-obvious decision) rather than narrating *what* the code already says, consistent with the
existing standard already followed by the more heavily-commented files in this codebase (e.g.
`TinyFlagsGrpcOptions.Validate`, `FeatureSnapshotEntityTag`). Not a rewrite pass — additive/
corrective only, no behavior changes. Includes two specific asks:

- `README.md`'s opening needs two strong sentences stating the actual thesis, not just "typed
  feature flags for .NET": TinyFlags is an open-source SDK that removes vendor lock-in on feature
  flags — the transport contracts (`IFeatureDefinitionsTransport`, `IFeatureValuesTransport`/
  `IFeatureValuesSubscription`) mean a team can implement their own transport for whatever
  provider or backend they already use, instead of being tied to one vendor's SDK and one vendor's
  server.
- Every public type/member across the four projects gets a real doc comment where it's missing one
  or has a weak one.

Proposed slices, one project per slice (doc pass only touches comments, so faster per-file than
step 2 — still stopping for review after each):
1. `README.md` opening (the two sentences) — small, standalone, first.
2. `src/TinyFlags` (core: `Abstractions/`, `DependencyInjection/`, `Registration/`,
   `Synchronization/`, root types) — 16 files.
3. `src/TinyFlags.Http` — 6 files.
4. `src/TinyFlags.Grpc` — 3 files.
5. `src/TinyFlags.SourceGen` — 20 files across `Analysis/`, `Discovery/`, `Diagnostics/`,
   `Generation/`, `Model/`, `Validation/`.

**Step 2 — code smells and clever code.** A second pass over the same four projects, this time
reading for `WORKING-AGREEMENT.md`'s engineering standard rather than doc coverage: overclever
one-liners, unnecessary abstraction, methods doing more than one job, unclear names, anything that
would make a reviewer stop and reread it twice. Per the working agreement, any abstraction found to
be unjustified gets flagged for discussion, not silently removed — and any new abstraction proposed
as a fix still needs the 4-point ritual before being introduced. Same per-project slice breakdown
as step 1, run after step 1 is fully approved.

Not started as of this note. No behavior changes are in scope for either step — this is a
readability/publishability pass, not a feature or a refactor. If a step 2 review surfaces an actual
bug (not just a smell), stop and raise it separately rather than folding a behavior fix into a
cleanup commit.
