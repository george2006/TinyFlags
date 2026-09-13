# Working agreement

## Engineering standard

We are two principal engineers building TinyFlags together. Prefer simple, robust, readable
object-oriented code. Good practices serve the product; they are not an exercise in purity.

- Objects represent concrete responsibilities and keep related behavior together.
- Read top down: entry point and main behavior first, implementation details below.
- Keep methods small and focused on one responsibility, without fragmenting a readable flow.
- Use intention-revealing names, braces, early returns, and whitespace between logical steps.
- Name conditions when doing so makes a decision easier to understand.
- Explain constraints and decisions in comments, rather than narrating obvious code.
- Accept small local duplication when sharing it would obscure the behavior.
- Make failure and cancellation behavior explicit. Do not claim guarantees we have not tested.

## Abstractions require discussion

Before creating a new abstraction, ask the user for permission and explain:

1. The current consumer and concrete problem.
2. The responsibility and proposed contract.
3. Why a concrete implementation or local code is insufficient.
4. The cost and simpler alternative.

Wait for approval before implementing that abstraction. Do not repeatedly ask for an already
approved contract unless its scope changes. Interfaces, base classes, wrappers, and extension
points must earn their place; do not create them for speculative reuse or mocking.

## Tests

- Test observable behavior, not private structure or sequences of mocked calls.
- Use real collaborators and real Roslyn compilations for generator tests.
- Check generated compilation, diagnostics, and executable behavior where relevant.
- Add focused regression tests for meaningful edge cases and discovered bugs.
- Do not add placeholder tests, tests of empty scaffolding, or assertions that merely mirror
  implementation details.
- Report exactly what ran, passed, failed, or remains unverified.

## Feature and slice workflow

Before implementing a feature, discuss its intended behavior and divide it into small,
reviewable slices with the user. Only the current feature gets an implementation breakdown;
future features stay as intent until we discuss them.

Before editing a slice, present its goal, existing code, proposed changes, behavioral checks,
scope boundaries, and decisions needed. An explicitly requested bootstrap can proceed within
that scope without asking for the same authorization again.

Then inspect the current files, implement the agreed slice, run appropriate checks, inspect
the resulting changes, and report the outcome. Stop for the user's review and approval before
starting another slice. Update `PLAN.md` to distinguish implementation from approval.

No commits, pushes, publishing, broad formatting, or unrelated cleanup unless requested.

## Generator structure

Use the TinySuite generators as references, not templates to copy wholesale:

- TinyValidations: discovery, analysis into a model, validation, planning, source emission.
- TinyEvents: consumer discovery/analysis, diagnostics, generation planning, source emission.
- TinyDispatcher: analysis, extraction, composition, validation, generation.

Keep Roslyn discovery and semantic analysis separate from generation planning and C# emission.
No Roslyn symbols may escape `Analysis`. Models and subsequent phases use our own data,
including scalar source positions and diagnostic information. Roslyn integration outside
analysis is limited to syntax filtering in `Discovery`, generator registration, and reporting
diagnostics to the compiler. Discovery is the initial syntax filter; Analysis resolves symbols
and produces plain models. The generator entry point wires both phases together.
Introduce only the phases and types the agreed behavior needs. A phase does not need an
interface just because it has a name. Prefer incremental generation and explicit generated
code over runtime assembly scanning when implementing the generator.

References reviewed in sibling repositories:

- `../TinyDispatcher/HowIWriteCode.md`
- `../TinyDispatcher/src/TinyDispatcher.SourceGen/Generator/GeneratorPipeline.cs`
- `../TinyValidations/docs/architecture.md`
- `../TinyValidations/src/TinyValidations.SourceGen/Generation/SourceGenerator.cs`
- `../TinyEvents/TinyEvents-Coding-Guide.md`
- `../TinyEvents/docs/way-of-working.md`
- `../TinyEvents/src/TinyEvents.SourceGen/Generation/TinyEventsSourceGenerator.cs`

These references inform the design; they are not dependencies of TinyFlags.
