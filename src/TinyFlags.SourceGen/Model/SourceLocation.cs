namespace TinyFlags.SourceGen.Model;

internal sealed class SourceLocation
{
    public SourceLocation(int treeIndex, int start, int length)
    {
        TreeIndex = treeIndex;
        Start = start;
        Length = length;
    }

    public int TreeIndex { get; }

    public int Start { get; }

    public int Length { get; }

    public override bool Equals(object? obj)
    {
        return obj is SourceLocation other
            && Equals(TreeIndex, other.TreeIndex)
            && Equals(Start, other.Start)
            && Equals(Length, other.Length);
    }

    public override int GetHashCode()
    {
        return (TreeIndex, Start, Length).GetHashCode();
    }
}
