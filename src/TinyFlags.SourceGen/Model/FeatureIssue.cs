namespace TinyFlags.SourceGen.Model;

internal sealed class FeatureIssue
{
    public FeatureIssue(string id, string memberName, SourceLocation location)
    {
        Id = id;
        MemberName = memberName;
        Location = location;
    }

    public string Id { get; }

    public string MemberName { get; }

    public SourceLocation Location { get; }

    public override bool Equals(object? obj)
    {
        return obj is FeatureIssue other
            && Equals(Id, other.Id)
            && Equals(MemberName, other.MemberName)
            && Equals(Location, other.Location);
    }

    public override int GetHashCode()
    {
        return (Id, MemberName, Location).GetHashCode();
    }
}
