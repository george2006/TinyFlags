using System.Collections.Immutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TinyFlags.SourceGen.Model;

namespace TinyFlags.SourceGen.Tests;

internal static class SourceGeneratorTestHost
{
    private static readonly ImmutableArray<MetadataReference> References = CreateReferences();

    public const string CatalogHintName = "TinyFlags.Generated.ThisAssemblyFeatureCatalog.g.cs";

    public static IEnumerable<GeneratedSourceResult> AccessSources(GeneratorDriverRunResult run)
    {
        return Assert.Single(run.Results).GeneratedSources.Where(source => source.HintName != CatalogHintName);
    }

    public static FeatureValidationResult Discover(params string[] sources)
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
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        Assert.Null(Assert.Single(driver.GetRunResult().Results).Exception);
        AssertCompiles(output);
        return driver;
    }

    public static FeatureValidationResult ReadDefinitions(GeneratorDriverRunResult run)
    {
        var steps = Assert.Single(run.Results).TrackedSteps;
        var results = steps.TryGetValue("FeatureValidation", out var validation)
            ? validation.SelectMany(step => step.Outputs)
                .Where(output => output.Reason != IncrementalStepRunReason.Removed)
                .Select(output => (FeatureValidationResult)output.Value).ToArray()
            : Array.Empty<FeatureValidationResult>();

        return new FeatureValidationResult(
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

        return compilation;
    }

    public static T Execute<T>(params string[] sources)
    {
        return ExecuteAssembly<T>(CompileAssembly(CreateCompilation(sources)));
    }

    public static byte[] CompileAssembly(Compilation compilation)
    {
        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        Assert.Empty(driver.GetRunResult().Diagnostics);
        AssertCompiles(output);

        using var stream = new MemoryStream();
        var emitted = output.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return stream.ToArray();
    }

    public static T ExecuteAssembly<T>(byte[] hostImage, params byte[][] libraries)
    {
        var loadContext = new AssemblyLoadContext("FlagConsumer", isCollectible: true);

        try
        {
            // Each scenario gets a real, isolated runtime registry, including its module initializers.
            loadContext.LoadFromAssemblyPath(typeof(IFeatureProvider).Assembly.Location);
            foreach (var library in libraries)
            {
                using var libraryStream = new MemoryStream(library);
                loadContext.LoadFromStream(libraryStream);
            }

            using var stream = new MemoryStream(hostImage);
            var assembly = loadContext.LoadFromStream(stream);
            var run = assembly.GetType("Scenario")!.GetMethod("Run")!;
            return Assert.IsType<T>(run.Invoke(null, null));
        }
        finally
        {
            loadContext.Unload();
        }
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
