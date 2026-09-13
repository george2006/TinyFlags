using Microsoft.CodeAnalysis;

namespace TinyFlags.SourceGen.Diagnostics;

internal static class FeatureDiagnostics
{
    public static readonly DiagnosticDescriptor UnsupportedProvider = new(
        "TFG001",
        "Unsupported feature provider",
        "Provider '{0}' must be a top-level, non-generic, concrete class with no base class other than object; records and file-local types are not supported",
        "TinyFlags",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedProperty = new(
        "TFG002",
        "Unsupported feature property",
        "Property '{0}' must be a public instance, non-indexed, read-only bool or non-nullable string property",
        "TinyFlags",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NonConstantDefault = new(
        "TFG003",
        "Feature default must be constant",
        "Property '{0}' must declare a non-null compile-time constant default matching its type",
        "TinyFlags",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
