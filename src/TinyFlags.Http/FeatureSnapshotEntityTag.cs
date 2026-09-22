using System;
using System.Globalization;

namespace TinyFlags;

/// <summary>
/// The wire-format ETag is fully derived from a snapshot's environment and revision; nothing else
/// carries independent information, so it is formatted on demand rather than stored.
/// </summary>
internal static class FeatureSnapshotEntityTag
{
    public static string Format(Guid environmentId, long revision)
        => $"\"{environmentId:D}:{revision.ToString(CultureInfo.InvariantCulture)}\"";
}
