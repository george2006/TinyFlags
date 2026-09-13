using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Tests;

internal static class SourceGeneratorTestHost
{
    private static readonly ImmutableArray<MetadataReference> References = CreateReferences();

    public static FeatureDiscoveryResult Discover(params string[] sources)
    {
        return ReadDefinitions(Run(sources));
    }

    public static GeneratorDriverRunResult Run(params string[] sources)
    {
        var compilation = CreateCompilation(sources);
        return Run(CreateDriver(), compilation).GetRunResult();
    }

    public static GeneratorDriver CreateDriver()
    {
        return CSharpGeneratorDriver.Create(
            generators: new[] { new TinyFlagsSourceGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
    }

    public static GeneratorDriver Run(GeneratorDriver driver, Compilation compilation)
    {
        AssertCompiles(compilation);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        AssertCompiles(output);
        Assert.Null(Assert.Single(driver.GetRunResult().Results).Exception);
        return driver;
    }

    public static FeatureDiscoveryResult ReadDefinitions(GeneratorDriverRunResult run)
    {
        var steps = Assert.Single(run.Results).TrackedSteps;
        var results = steps.TryGetValue("FeatureValidation", out var validation)
            ? validation.SelectMany(step => step.Outputs)
                .Where(output => output.Reason != IncrementalStepRunReason.Removed)
                .Select(output => (FeatureDiscoveryResult)output.Value).ToArray()
            : Array.Empty<FeatureDiscoveryResult>();

        return new FeatureDiscoveryResult(
            results.SelectMany(result => result.Providers).ToImmutableArray(),
            results.SelectMany(result => result.Diagnostics).ToImmutableArray());
    }

    public static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var trees = sources.Select((source, index) => CSharpSyntaxTree.ParseText(
            source, path: $"Declaration{index}.cs"));
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);
        var compilation = CSharpCompilation.Create("FlagConsumer", trees, References, options);

        AssertCompiles(compilation);
        return compilation;
    }

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var platformAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);

        return platformAssemblies
            .Append(typeof(IFeatureProvider).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();
    }

    private static void AssertCompiles(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        Assert.True(!errors.Any(), string.Join(Environment.NewLine, errors));
    }
}
