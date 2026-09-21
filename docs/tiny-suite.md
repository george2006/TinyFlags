# Tiny suite

TinyFlags belongs to the Tiny suite:

| Project | Kind | Responsibility |
| --- | --- | --- |
| [TinyDispatcher](https://github.com/george2006/TinyDispatcher) | Library | Command and query execution |
| [TinyValidations](https://github.com/george2006/TinyValidations) | Library | Application input validation |
| [TinyEvents](https://github.com/george2006/TinyEvents) | Library | Reliable application-event handling through the outbox pattern |
| [TinyFlags](https://github.com/george2006/TinyFlags) | Library + Server | Typed feature flag declarations, with an optional server for centrally managed values |
| [TheTinyApplicationLayer](https://github.com/george2006/TheTinyApplicationLayer) | Example | Runnable ASP.NET Core and Blazor application using the suite |

Same author, same philosophy across all of them: move what can fail to compile time, and keep
runtime execution explicit rather than stringly-typed or reflection-driven.

- TinyDispatcher does it for command/query dispatch.
- TinyValidations does it for input validation.
- TinyEvents does it for the outbox pattern.
- TinyFlags does it for feature flags: the code declares the flag as a typed property, so a
  renamed or missing flag is a compile error, not a runtime lookup that silently falls through to
  a default.

## Standing apart, on purpose

Unlike the other three libraries, TinyFlags has no SDK-level dependency on its siblings. It does
not appear in `TheTinyApplicationLayer`'s shared sample yet — that integration is planned once this
feature set settles, not implemented today. Adopting TinyFlags does not require adopting anything
else from the suite.

One internal detail worth knowing: `TinyFlags.Server` is itself built with TinyDispatcher for its
own command/query handling (`Features/RegisterDefinitions`, `Features/GetFeatureValues`). That is
an implementation choice inside the server, not a dependency your application takes on by using
the TinyFlags client.
