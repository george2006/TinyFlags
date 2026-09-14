using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace TinyFlags;

/// <summary>
/// Collects generated contributions from initialized assemblies for the root application.
/// </summary>
public static class TinyFlagsBootstrap
{
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<Type, (FeatureDefinition[] Definitions, Action<IServiceCollection> Register)> Contributions = new();

    /// <summary>
    /// Registers a snapshot of an assembly's definitions. The first registration for a type wins.
    /// </summary>
    public static void AddContribution(Type contributionType, IReadOnlyList<FeatureDefinition> definitions)
    {
        AddContribution(contributionType, definitions, static _ => { });
    }

    /// <summary>
    /// Registers definitions and a service registration callback as one contribution.
    /// Callbacks must support repeated application to the same service collection.
    /// </summary>
    public static void AddContribution(
        Type contributionType,
        IReadOnlyList<FeatureDefinition> definitions,
        Action<IServiceCollection> register)
    {
        ArgumentNullException.ThrowIfNull(contributionType);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(register);
        var snapshot = CopyDefinitions(definitions);

        lock (SyncRoot)
        {
            Contributions.TryAdd(contributionType, (snapshot, register));
        }
    }

    /// <summary>
    /// Returns an immutable, sorted snapshot. Equivalent keys are combined; conflicting definitions fail.
    /// This does not load or initialize assemblies.
    /// </summary>
    public static IReadOnlyList<FeatureDefinition> GetDefinitions()
    {
        FeatureDefinition[][] contributions;
        lock (SyncRoot)
        {
            contributions = Contributions.Values.Select(contribution => contribution.Definitions).ToArray();
        }

        return ComposeDefinitions(contributions);
    }

    internal static void Apply(IServiceCollection services)
    {
        Action<IServiceCollection>[] registrations;
        lock (SyncRoot)
        {
            registrations = Contributions.Values.Select(contribution => contribution.Register).ToArray();
        }

        foreach (var register in registrations)
        {
            register(services);
        }
    }

    private static FeatureDefinition[] CopyDefinitions(IReadOnlyList<FeatureDefinition> definitions)
    {
        var snapshot = new FeatureDefinition[definitions.Count];
        for (var index = 0; index < definitions.Count; index++)
        {
            snapshot[index] = definitions[index]
                ?? throw new ArgumentException("A contribution cannot contain a null definition.", nameof(definitions));
        }

        return snapshot;
    }

    private static IReadOnlyList<FeatureDefinition> ComposeDefinitions(FeatureDefinition[][] contributions)
    {
        var definitions = new Dictionary<string, FeatureDefinition>(StringComparer.Ordinal);
        foreach (var contribution in contributions)
        {
            foreach (var definition in contribution)
            {
                AddDefinition(definitions, definition);
            }
        }

        return Array.AsReadOnly(definitions.Values.OrderBy(definition => definition.Key, StringComparer.Ordinal).ToArray());
    }

    private static void AddDefinition(Dictionary<string, FeatureDefinition> definitions, FeatureDefinition definition)
    {
        if (!definitions.TryGetValue(definition.Key, out var existing))
        {
            definitions.Add(definition.Key, definition);
            return;
        }

        var hasSameDefault = existing.Kind == definition.Kind
            && Equals(existing.DefaultValue, definition.DefaultValue);
        if (!hasSameDefault)
        {
            throw new InvalidOperationException(
                $"Feature '{definition.Key}' has conflicting definitions across contributions.");
        }
    }
}
