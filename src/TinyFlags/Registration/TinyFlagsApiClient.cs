using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TinyFlags;

internal sealed class TinyFlagsApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<FeatureKind>() }
    };

    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly TinyFlagsClientOptions options;
    private readonly Uri registrationEndpoint;
    private readonly Uri valuesEndpoint;
    private readonly TinyFlagsRetryPolicy retry;
    private readonly FeatureSnapshotReader snapshotReader;
    private readonly ILogger logger;

    public TinyFlagsApiClient(TinyFlagsClientOptions options, FeatureSnapshotReader snapshotReader, ILogger? logger = null)
        : this(CreateHttpClient(options, snapshotReader), options, snapshotReader, logger) => ownsHttpClient = true;

    public TinyFlagsApiClient(HttpClient httpClient, TinyFlagsClientOptions options,
        FeatureSnapshotReader snapshotReader, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(snapshotReader);
        this.httpClient = httpClient;
        this.options = options.CreateSnapshot();
        registrationEndpoint = new Uri(this.options.Endpoint!, "v1/client/definitions");
        valuesEndpoint = new Uri(this.options.Endpoint!, "v1/client/values");
        retry = new TinyFlagsRetryPolicy(this.options);
        this.snapshotReader = snapshotReader;
        this.logger = logger ?? NullLogger.Instance;
    }

    public async Task RegisterDefinitionsAsync(IReadOnlyList<FeatureDefinition> definitions, CancellationToken ct = default)
    {
        var catalog = CopyCatalog(definitions);
        await retry.ExecuteAsync(token => SendRegistrationAsync(catalog, token),
            ReadRegistrationAsync, RequestTimeout(), logger, ct).ConfigureAwait(false);
    }

    public Task<FeatureValuesResult> GetValuesAsync(IReadOnlyList<FeatureDefinition> catalog,
        FeatureSnapshot? current = null, CancellationToken ct = default)
    {
        var definitions = CopyCatalog(catalog);
        return retry.ExecuteAsync(token => SendValuesAsync(current, token),
            (response, token) => ReadValuesAsync(response, definitions, current, token), RequestTimeout(), logger, ct);
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendRegistrationAsync(FeatureDefinition[] definitions, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, registrationEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Content = JsonContent.Create(new { definitions }, options: JsonOptions);
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendValuesAsync(FeatureSnapshot? current, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, valuesEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        if (current is not null)
        {
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(current.EntityTag));
        }
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
    }

    private static Task<bool> ReadRegistrationAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode != HttpStatusCode.NoContent)
        {
            throw RejectedResponse(response.StatusCode);
        }
        return Task.FromResult(true);
    }

    private async Task<FeatureValuesResult> ReadValuesAsync(HttpResponseMessage response,
        IReadOnlyList<FeatureDefinition> catalog, FeatureSnapshot? current, CancellationToken ct)
    {
        try
        {
            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                snapshotReader.ValidateUnchanged(response, current);
                return FeatureValuesResult.Unchanged();
            }
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw RejectedResponse(response.StatusCode);
            }

            var snapshot = await snapshotReader.ReadAsync(response, catalog, ct).ConfigureAwait(false);
            if (current is not null && snapshot.EnvironmentId != current.EnvironmentId)
            {
                throw new TinyFlagsClientException(TinyFlagsClientFailure.InvalidResponse);
            }
            return current is not null && snapshot.Revision <= current.Revision
                ? FeatureValuesResult.Unchanged()
                : FeatureValuesResult.Updated(snapshot);
        }
        catch (JsonException)
        {
            throw new TinyFlagsClientException(TinyFlagsClientFailure.InvalidResponse);
        }
    }

    private static TinyFlagsClientException RejectedResponse(HttpStatusCode status)
        => new(status switch
        {
            HttpStatusCode.Unauthorized => TinyFlagsClientFailure.CredentialsRejected,
            HttpStatusCode.Forbidden => TinyFlagsClientFailure.AccessDenied,
            HttpStatusCode.Conflict => TinyFlagsClientFailure.DefinitionsConflict,
            >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError => TinyFlagsClientFailure.RequestRejected,
            _ => TinyFlagsClientFailure.InvalidResponse
        });

    private TimeSpan RequestTimeout()
        => httpClient.Timeout != Timeout.InfiniteTimeSpan && httpClient.Timeout < options.RequestTimeout
            ? httpClient.Timeout : options.RequestTimeout;

    private static FeatureDefinition[] CopyCatalog(IReadOnlyList<FeatureDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var snapshot = definitions.ToArray();
        foreach (var definition in snapshot)
        {
            ArgumentNullException.ThrowIfNull(definition);
        }
        return snapshot;
    }

    private static HttpClient CreateHttpClient(TinyFlagsClientOptions options, FeatureSnapshotReader snapshotReader)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(snapshotReader);
        options.Validate();
        return new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    }
}
