using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TinyFlags;

internal sealed class TinyFlagsApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter<FeatureKind>() }
    };

    private readonly HttpClient httpClient;
    private readonly Uri registrationEndpoint;
    private readonly string apiKey;
    private readonly TimeSpan requestTimeout;

    public TinyFlagsApiClient(HttpClient httpClient, TinyFlagsClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.httpClient = httpClient;
        registrationEndpoint = new Uri(new Uri(options.Endpoint!.AbsoluteUri.TrimEnd('/') + "/"), "v1/client/definitions");
        apiKey = options.ApiKey!;
        requestTimeout = options.RequestTimeout;
    }

    public async Task<HttpResponseMessage> RegisterDefinitionsAsync(
        IReadOnlyList<FeatureDefinition> definitions, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ct.ThrowIfCancellationRequested();
        var snapshot = definitions.ToArray();
        foreach (var definition in snapshot)
        {
            ArgumentNullException.ThrowIfNull(definition);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, registrationEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new { definitions = snapshot }, options: JsonOptions);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(requestTimeout);
        // The caller owns the response and decides whether its status merits a retry.
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
    }
}
