using System;
using System.Globalization;

namespace TinyFlags;

internal static class FeatureSnapshotEntityTag
{
    public static string Format(Guid environmentId, long revision)
        => $"\"{environmentId:D}:{revision.ToString(CultureInfo.InvariantCulture)}\"";
}
