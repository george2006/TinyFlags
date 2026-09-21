using System.Globalization;
using System.Text;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Generation.Emission;

internal static class FeatureLiteralWriter
{
    public static void WriteDefaultValue(StringBuilder source, FeatureValueKind kind, object defaultValue)
    {
        if (kind == FeatureValueKind.Boolean)
        {
            source.Append((bool)defaultValue ? "true" : "false");
        }
        else
        {
            WriteString(source, (string)defaultValue);
        }
    }

    public static void WriteString(StringBuilder source, string value)
    {
        source.Append('"');
        foreach (var character in value)
        {
            if (character == '"' || character == '\\')
            {
                source.Append('\\').Append(character);
            }
            else if (character < ' ' || character > '~')
            {
                source.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                source.Append(character);
            }
        }

        source.Append('"');
    }
}
