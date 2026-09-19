using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TinyFlags;

internal sealed class FeatureSnapshotReader
{
    private readonly int maxSnapshotBytes;

    public FeatureSnapshotReader(int maxSnapshotBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSnapshotBytes);
        this.maxSnapshotBytes = maxSnapshotBytes;
    }

    public async Task<FeatureSnapshot> ReadAsync(HttpResponseMessage response,
        IReadOnlyList<FeatureDefinition> catalog, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(catalog);
        ct.ThrowIfCancellationRequested();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new JsonException("A feature snapshot requires HTTP 200.");
        }
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException("A feature snapshot requires JSON content.");
        }

        using var buffer = await ReadBodyAsync(response.Content, ct).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(buffer, cancellationToken: ct).ConfigureAwait(false);
        var root = document.RootElement;
        ValidateObject(root);
        var revision = ReadRevision(root);
        var (environmentId, entityTag) = ReadEntityTag(response, revision);
        var values = ReadValues(root, catalog, ct);
        ct.ThrowIfCancellationRequested();
        return new FeatureSnapshot(environmentId, revision, entityTag, values);
    }

    public void ValidateUnchanged(HttpResponseMessage response, FeatureSnapshot? current)
    {
        if (current is null)
        {
            throw new JsonException("An unchanged response requires an accepted snapshot.");
        }
        var (environmentId, _) = ReadEntityTag(response, current.Revision);
        if (environmentId != current.EnvironmentId)
        {
            throw new JsonException("The unchanged response belongs to another environment.");
        }
    }

    private async Task<MemoryStream> ReadBodyAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maxSnapshotBytes)
        {
            throw new JsonException("The feature snapshot exceeds MaxSnapshotBytes.");
        }

        using var body = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new MemoryStream();
        try
        {
            var chunk = new byte[8192];
            int read;
            while ((read = await body.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + read > maxSnapshotBytes)
                {
                    throw new JsonException("The feature snapshot exceeds MaxSnapshotBytes.");
                }
                buffer.Write(chunk, 0, read);
            }
            buffer.Position = 0;
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    private static long ReadRevision(JsonElement root)
    {
        var value = RequiredProperty(root, "revision");
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var revision) || revision < 0)
        {
            throw new JsonException("A feature snapshot requires a nonnegative integer revision.");
        }
        return revision;
    }

    private static (Guid EnvironmentId, string ETag) ReadEntityTag(HttpResponseMessage response, long revision)
    {
        var headers = response.Headers.TryGetValues("ETag", out var values) ? values.ToArray() : Array.Empty<string>();
        if (headers.Length != 1 || !EntityTagHeaderValue.TryParse(headers[0], out var tag))
        {
            throw new JsonException("A feature snapshot requires one valid ETag.");
        }

        var parts = tag.Tag.Trim('"').Split(':');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "D", out var environmentId) || environmentId == Guid.Empty)
        {
            throw new JsonException("The snapshot ETag requires an environment identity.");
        }

        var expected = $"\"{environmentId:D}:{revision.ToString(CultureInfo.InvariantCulture)}\"";
        if (tag.Tag != expected)
        {
            throw new JsonException("The snapshot revision and ETag do not match.");
        }
        return (environmentId, tag.ToString());
    }

    private static Dictionary<string, object> ReadValues(JsonElement root,
        IReadOnlyList<FeatureDefinition> catalog, CancellationToken ct)
    {
        var entries = RequiredProperty(root, "values");
        if (entries.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("A feature snapshot requires a values array.");
        }

        var knownKinds = catalog.ToDictionary(definition => definition.Key, definition => definition.Kind, StringComparer.Ordinal);
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var entry in entries.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            ValidateObject(entry);
            var key = ReadString(entry, "key");
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new JsonException("Feature keys cannot be blank.");
            }

            var kind = ReadKind(entry);
            if (knownKinds.TryGetValue(key, out var knownKind) && knownKind != kind)
            {
                throw new JsonException("A feature kind conflicts with its declaration.");
            }
            if (!values.TryAdd(key, ReadValue(entry, kind)))
            {
                throw new JsonException("Feature keys must be unique.");
            }
        }
        return values;
    }

    private static FeatureKind ReadKind(JsonElement entry)
        => ReadString(entry, "kind") switch
        {
            "Boolean" => FeatureKind.Boolean,
            "String" => FeatureKind.String,
            _ => throw new JsonException("Unsupported feature kind.")
        };

    private static object ReadValue(JsonElement entry, FeatureKind kind)
    {
        var value = RequiredProperty(entry, "value");
        return (kind, value.ValueKind) switch
        {
            (FeatureKind.Boolean, JsonValueKind.True or JsonValueKind.False) => value.GetBoolean(),
            (FeatureKind.String, JsonValueKind.String) => value.GetString()!,
            _ => throw new JsonException("A feature value does not match its kind.")
        };
    }

    private static string ReadString(JsonElement entry, string property)
    {
        var value = RequiredProperty(entry, property);
        return value.ValueKind == JsonValueKind.String ? value.GetString()!
            : throw new JsonException("A snapshot property requires a string.");
    }

    private static JsonElement RequiredProperty(JsonElement entry, string property)
        => entry.TryGetProperty(property, out var value) ? value : throw new JsonException("A required snapshot property is missing.");

    private static void ValidateObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A snapshot object is required.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new JsonException("Duplicate snapshot properties are not supported.");
            }
        }
    }

}
