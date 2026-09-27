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
internal sealed class TinyFlagsGrpcTransport :
    IFeatureDefinitionsTransport,
    IFeatureValuesSubscription,
    IDisposable
{
    private readonly GrpcChannel channel;
    private readonly TinyFlagsDefinitions.TinyFlagsDefinitionsClient definitionsClient;
    private readonly TinyFlagsValues.TinyFlagsValuesClient valuesClient;
    private readonly TinyFlagsGrpcOptions options;
    private readonly GrpcFeatureSnapshotReader snapshotReader = new();

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
            AppContext.SetSwitch(
                "System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport",
                true);
        }

        channel = GrpcChannel.ForAddress(options.Endpoint);
        definitionsClient =
            new TinyFlagsDefinitions.TinyFlagsDefinitionsClient(channel);
        valuesClient =
            new TinyFlagsValues.TinyFlagsValuesClient(channel);
    }

    public async Task RegisterAsync(
        IReadOnlyList<FeatureDefinition> definitions,
        CancellationToken ct = default)
    {
        var request = new RegisterRequest();
        request.Definitions.AddRange(definitions.Select(ToProtoDefinition));

        try
        {
            await definitionsClient
                .RegisterAsync(request, CreateCallOptions(ct))
                .ConfigureAwait(false);
        }
        catch (RpcException error)
        {
            throw new TinyFlagsClientException(ToFailure(error.StatusCode));
        }
    }

    public async IAsyncEnumerable<FeatureValuesResult> WatchAsync(
        IReadOnlyList<FeatureDefinition> catalog,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        FeatureValuesCursor? current = null;

        while (!ct.IsCancellationRequested)
        {
            using var call =
                valuesClient.Watch(new WatchRequest(), CreateCallOptions(ct));

            await using var enumerator = call.ResponseStream
                .ReadAllAsync(ct)
                .GetAsyncEnumerator(ct);

            while (true)
            {
                var (outcome, value) =
                    await StepAsync(enumerator, catalog, current, ct)
                        .ConfigureAwait(false);

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

            await Task.Delay(options.ReconnectDelay, ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads one stream item and translates stream failures into watch outcomes.
    /// This lives outside WatchAsync because C# does not allow yield return inside
    /// a try block with a catch.
    /// </summary>
    private async Task<(StepOutcome Outcome, FeatureValuesResult? Value)> StepAsync(
        IAsyncEnumerator<ValuesSnapshot> enumerator,
        IReadOnlyList<FeatureDefinition> catalog,
        FeatureValuesCursor? current,
        CancellationToken ct)
    {
        try
        {
            if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                return (StepOutcome.Completed, null);
            }

            var result = snapshotReader.Read(enumerator.Current, catalog);

            if (BelongsToAnotherEnvironment(result, current))
            {
                throw new TinyFlagsClientException(
                    TinyFlagsClientFailure.InvalidResponse);
            }

            if (IsStale(result, current))
            {
                return (StepOutcome.Stale, null);
            }

            return (StepOutcome.Value, result);
        }
        catch (RpcException error)
            when (IsTransient(error.StatusCode) && !ct.IsCancellationRequested)
        {
            return (StepOutcome.Reconnect, null);
        }
        catch (RpcException error)
        {
            throw new TinyFlagsClientException(ToFailure(error.StatusCode));
        }
    }

    private static bool BelongsToAnotherEnvironment(
        FeatureValuesResult result,
        FeatureValuesCursor? current)
    {
        return current is not null &&
               result.Cursor!.EnvironmentId != current.EnvironmentId;
    }

    private static bool IsStale(
        FeatureValuesResult result,
        FeatureValuesCursor? current)
    {
        return current is not null &&
               result.Cursor!.Revision <= current.Revision;
    }

    private static bool IsTransient(StatusCode status)
    {
        return status is
            StatusCode.Unavailable or
            StatusCode.DeadlineExceeded or
            StatusCode.Internal or
            StatusCode.Aborted;
    }

    private CallOptions CreateCallOptions(CancellationToken ct)
    {
        return new CallOptions(
            headers: new Metadata
            {
                { "authorization", $"Bearer {options.ApiKey}" }
            },
            cancellationToken: ct);
    }

    private static ProtoFeatureDefinition ToProtoDefinition(
        FeatureDefinition definition)
    {
        var message = new ProtoFeatureDefinition
        {
            Key = definition.Key,
            Kind = definition.Kind == FeatureKind.Boolean
                ? ProtoFeatureKind.Boolean
                : ProtoFeatureKind.String
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

    private static TinyFlagsClientFailure ToFailure(StatusCode status)
    {
        return status switch
        {
            StatusCode.Unauthenticated =>
                TinyFlagsClientFailure.CredentialsRejected,

            StatusCode.PermissionDenied =>
                TinyFlagsClientFailure.AccessDenied,

            StatusCode.AlreadyExists =>
                TinyFlagsClientFailure.DefinitionsConflict,

            StatusCode.InvalidArgument or StatusCode.ResourceExhausted =>
                TinyFlagsClientFailure.RequestRejected,

            _ =>
                TinyFlagsClientFailure.InvalidResponse
        };
    }

    public void Dispose()
    {
        channel.Dispose();
    }

    private enum StepOutcome
    {
        Value,
        Completed,
        Reconnect,
        Stale
    }
}