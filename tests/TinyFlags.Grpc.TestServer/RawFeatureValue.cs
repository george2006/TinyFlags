namespace TinyFlags.Grpc.TestServer;

/// <summary>
/// A deliberately permissive stand-in for the wire's FeatureValue message: Kind and the two value
/// slots are independent fields here, not a real oneof, so a test can construct shapes the real
/// client is supposed to reject - kind/value mismatch, or neither value set - without this project
/// needing to expose the real protobuf type to the test project (see this project's csproj comment
/// for why that would conflict).
/// </summary>
public readonly record struct RawFeatureValue(string Key, RawFeatureKind Kind, bool? BoolValue, string? StringValue)
{
    public static RawFeatureValue Bool(string key, bool value) => new(key, RawFeatureKind.Boolean, value, null);

    public static RawFeatureValue String(string key, string value) => new(key, RawFeatureKind.String, null, value);
}

public enum RawFeatureKind
{
    Unspecified,
    Boolean,
    String
}
