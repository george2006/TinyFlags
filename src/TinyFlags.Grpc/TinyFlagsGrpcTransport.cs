using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using global::Grpc.Core;
using global::Grpc.Net.Client;
using TinyFlags.Grpc;
using ProtoFeatureDefinition = TinyFlags.Grpc.FeatureDefinition;
using ProtoFeatureKind = TinyFlags.Grpc.FeatureKind;

namespace TinyFlags;

/// <summary>
/// Implements both gRPC services from tinyflags.proto against one channel - registration is a
/// single request/response call; Watch reconnects on transient failures (the contract's job, not
/// TinyFlagsValuesWatchWorker's - it just drains whatever this yields) but lets permanent
/// failures (bad credentials, etc.) propagate as TinyFlagsClientException so the worker stops.
/// </summary>
internal sealed class TinyFlagsGrpcTransport : IFeatureDefinitionsTransport, IFeatureValuesSubscription, IDisposable
{
    private readonly GrpcChannel channel;
    private readonly TinyFlagsDefinitions.TinyFlagsDefinitionsClient definitionsClient;
    private readonly TinyFlagsValues.TinyFlagsValuesClient valuesClient;
    private readonly TinyFlagsGrpcOptions options;

    public TinyFlagsGrpcTransport(TinyFlagsGrpcOptions options)
    {
        this.options = options;
        if (options.Endpoint!.Scheme == Uri.UriSchemeHttp)
        {
            // Only ever reached for loopback (Validate() rejects http elsewhere) - .NET's
            // HttpClient otherwise silently refuses to even attempt HTTP/2 over plain http://,
            // which fails every call with no useful error pointing at why. This switch is
            // process-wide, not scoped to this channel - setting it here also enables cleartext
            // HTTP/2 for any other HttpClient in the same process, an unavoidable consequence of
            // .NET not offering a per-handler equivalent.
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        }
        channel = GrpcChannel.ForAddress(options.Endpoint);
        definitionsClient = new TinyFlagsDefinitions.TinyFlagsDefinitionsClient(channel);
        valuesClient = new TinyFlagsValues.TinyFlagsValuesClient(channel);
    }

    public async Task RegisterAsync(IReadOnlyList<FeatureDefinition> definitions, CancellationToken ct = default)
    {
        var request = new RegisterRequest();
        request.Definitions.AddRange(definitions.Select(ToProtoDefinition));
        try
        {
            await definitionsClient.RegisterAsync(request, CreateCallOptions(ct)).ConfigureAwait(false);
        }
        catch (RpcException error)
        {
            throw new TinyFlagsClientException(ToFailure(error.StatusCode));
        }
    }

    public async IAsyncEnumerable<FeatureValuesResult> WatchAsync(IReadOnlyList<FeatureDefinition> catalog,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        FeatureValuesCursor? current = null;
        while (!ct.IsCancellationRequested)
        {
            using var call = valuesClient.Watch(new WatchRequest(), CreateCallOptions(ct));
            await using (var enumerator = call.ResponseStream.ReadAllAsync(ct).GetAsyncEnumerator(ct))
            {
                while (true)
                {
                    var (outcome, value) = await StepAsync(enumerator, catalog, current, ct).ConfigureAwait(false);
                    if (outcome == StepOutcome.Completed)
                    {
                        yield break;
                    }
                    if (outcome == StepOutcome.Reconnect)
                    {
                        break;
                    }
                    if (outcome == StepOutcome.Stale)
                    {
                        continue;
                    }
                    current = value!.Cursor;
                    yield return value;
                }
            }
            await Task.Delay(options.ReconnectDelay, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// One step of the watch loop, isolated in its own method rather than inline in
    /// <see cref="WatchAsync"/>: C# does not allow <c>yield return</c> inside a <c>try</c> block
    /// that has a <c>catch</c>, so the per-step exception handling that decides reconnect versus
    /// permanent failure has to live here and hand back an outcome instead.
    /// </summary>
    private static async Task<(StepOutcome Outcome, FeatureValuesResult? Value)> StepAsync(
        IAsyncEnumerator<ValuesSnapshot> enumerator, IReadOnlyList<FeatureDefinition> catalog,
        FeatureValuesCursor? current, CancellationToken ct)
    {
        try
        {
            if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                return (StepOutcome.Completed, null);
            }

            var result = ToResult(enumerator.Current, catalog);
            // Cross-checked on every message, not just the first - catches a misbehaving server
            // or a credential that started pointing at a different environment mid-stream, not
            // just a mismatch at connect time.
            if (current is not null && result.Cursor!.EnvironmentId != current.EnvironmentId)
            {
                throw new TinyFlagsClientException(TinyFlagsClientFailure.InvalidResponse);
            }
            // A reconnect (or a lagging replica) can hand back a snapshot the caller already has
            // or has already moved past - never roll values backward. This is not the same as the
            // HTTP transport's Unchanged(): a push transport must never yield Unchanged (see
            // building-a-transport.md), so a stale message is silently skipped instead, same as a
            // transient reconnect, rather than surfaced as a value or an error.
            if (current is not null && result.Cursor!.Revision <= current.Revision)
            {
                return (StepOutcome.Stale, null);
            }
            return (StepOutcome.Value, result);
        }
        catch (RpcException error) when (IsTransient(error.StatusCode) && !ct.IsCancellationRequested)
        {
            return (StepOutcome.Reconnect, null);
        }
        catch (RpcException error)
        {
            throw new TinyFlagsClientException(ToFailure(error.StatusCode));
        }
    }

    private enum StepOutcome { Value, Completed, Reconnect, Stale }

    private static bool IsTransient(StatusCode status)
        => status is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Internal or StatusCode.Aborted;

    private CallOptions CreateCallOptions(CancellationToken ct)
        => new(headers: new Metadata { { "authorization", $"Bearer {options.ApiKey}" } }, cancellationToken: ct);

    private static ProtoFeatureDefinition ToProtoDefinition(FeatureDefinition definition)
    {
        var message = new ProtoFeatureDefinition
        {
            Key = definition.Key,
            Kind = definition.Kind == FeatureKind.Boolean ? ProtoFeatureKind.Boolean : ProtoFeatureKind.String
        };
        if (definition.DefaultValue is bool boolValue)
        {
            message.BoolDefault = boolValue;
        }
        else
        {
            message.StringDefault = (string)definition.DefaultValue;
        }
        return message;
    }

    /// <summary>
    /// Validates explicitly rather than letting a malformed message throw an unclassified
    /// exception, or silently accepting one - mirrors FeatureSnapshotReader's strictness on the
    /// HTTP side, so both reference transports report the same TinyFlagsClientFailure.InvalidResponse
    /// for the same failure class. Unlike HTTP, a valid but unrecognized key is still accepted
    /// (docs/protocol.md's "valid unknown keys are allowed" rule applies here too); only a known
    /// key whose kind disagrees with the local declaration is rejected.
    /// </summary>
    private static FeatureValuesResult ToResult(ValuesSnapshot snapshot, IReadOnlyList<FeatureDefinition> catalog)
    {
        if (!Guid.TryParse(snapshot.EnvironmentId, out var environmentId) || environmentId == Guid.Empty)
        {
            throw new TinyFlagsClientException(TinyFlagsClientFailure.InvalidResponse);
        }
        if (snapshot.Revision < 0)
        {
            throw new TinyFlagsClientException(TinyFlagsClientFailure.InvalidResponse);
        }

        var knownKinds = catalog.ToDictionary(definition => definition.Key, definition => definition.Kind, StringComparer.Ordinal);
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var value in snapshot.Values)
        {
            if (string.IsNullOrWhiteSpace(value.Key))
            {
                throw new TinyFlagsClientException(TinyFlagsClientFailure.InvalidResponse);
            }

            var (kind, typedValue) = ToKindAndValue(value);
            if (knownKinds.TryGetValue(value.Key, out var knownKind) && knownKind != kind)
            {
                throw new TinyFlagsClientException(TinyFlagsClientFailure.InvalidResponse);
            }
            if (!values.TryAdd(value.Key, typedValue))
            {
                throw new TinyFlagsClientException(TinyFlagsClientFailure.InvalidResponse);
            }
        }
        return FeatureValuesResult.Updated(new FeatureValuesCursor(environmentId, snapshot.Revision), values);
    }

    /// <summary>
    /// Reads Kind and the oneof together, rejecting either alone: an unset oneof (ValueCase.None)
    /// would otherwise silently read as an empty string rather than an error, and Kind disagreeing
    /// with which case is actually set (e.g. Boolean claimed while a string is carried) would
    /// otherwise go unnoticed since nothing else in the message shape links the two.
    /// </summary>
    private static (FeatureKind Kind, object Value) ToKindAndValue(FeatureValue value) => (value.Kind, value.ValueCase) switch
    {
        (ProtoFeatureKind.Boolean, FeatureValue.ValueOneofCase.BoolValue) => (FeatureKind.Boolean, value.BoolValue),
        (ProtoFeatureKind.String, FeatureValue.ValueOneofCase.StringValue) => (FeatureKind.String, value.StringValue),
        _ => throw new TinyFlagsClientException(TinyFlagsClientFailure.InvalidResponse)
    };

    private static TinyFlagsClientFailure ToFailure(StatusCode status) => status switch
    {
        StatusCode.Unauthenticated => TinyFlagsClientFailure.CredentialsRejected,
        StatusCode.PermissionDenied => TinyFlagsClientFailure.AccessDenied,
        StatusCode.AlreadyExists => TinyFlagsClientFailure.DefinitionsConflict,
        StatusCode.InvalidArgument or StatusCode.ResourceExhausted => TinyFlagsClientFailure.RequestRejected,
        _ => TinyFlagsClientFailure.InvalidResponse
    };

    public void Dispose() => channel.Dispose();
}
