namespace Ardea.Architecture.Tests;

/// <summary>
/// Rules 1 to 4 of master plan 3.2: modules only see each other's Contracts, dependencies point
/// one way, and source adapters only know the provider SDK.
/// </summary>
public sealed class ModuleBoundaryTests
{
    /// <summary>Which other modules' Contracts a module may use (master plan 3.2, rule 3).</summary>
    private static readonly IReadOnlyDictionary<string, string[]> AllowedContracts = new Dictionary<string, string[]>
    {
        ["Iam"] = [],
        ["Catalog"] = [],
        ["Intel"] = ["Catalog"],
        ["Assets"] = ["Iam", "Catalog"],
        ["Assessment"] = ["Intel", "Assets", "Catalog", "Iam"],
        ["Notifications"] = ["Assessment", "Iam", "Intel"],
    };

    public static TheoryData<string> ModuleNames => [.. AllowedContracts.Keys];

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_references_only_platform_and_allowed_contracts(string module)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Ardea.SharedKernel",
            "Ardea.Platform",
            $"Ardea.{module}.Contracts",
        };
        allowed.UnionWith(AllowedContracts[module].Select(m => $"Ardea.{m}.Contracts"));
        if (module == "Intel")
        {
            allowed.Add("Ardea.Intel.Sources.Abstractions");
        }

        var violations = Assemblies.ArdeaReferences(Assemblies.Modules[module])
            .Where(r => !allowed.Contains(r))
            .ToList();

        Assert.True(violations.Count == 0, $"Ardea.{module} must not reference: {string.Join(", ", violations)}");
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Contracts_reference_only_the_shared_kernel(string module)
    {
        var violations = Assemblies.ArdeaReferences(Assemblies.Contracts(module))
            .Where(r => r != "Ardea.SharedKernel")
            .ToList();

        Assert.True(violations.Count == 0, $"Ardea.{module}.Contracts must not reference: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Source_adapters_reference_only_the_provider_sdk()
    {
        string[] allowed = ["Ardea.SharedKernel", "Ardea.Intel.Sources.Abstractions", "Ardea.Intel.Sources.Formats"];

        var violations = Assemblies.AllSources()
            .Where(a => a.GetName().Name != "Ardea.Intel.Sources.Abstractions")
            .SelectMany(a => Assemblies.ArdeaReferences(a)
                .Where(r => !allowed.Contains(r))
                .Select(r => $"{a.GetName().Name} -> {r}"))
            .ToList();

        Assert.True(violations.Count == 0, $"Source adapters must not reference: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Platform_and_shared_kernel_know_no_module()
    {
        var violations = Assemblies.ArdeaReferences(Assemblies.Platform)
            .Concat(Assemblies.ArdeaReferences(Assemblies.SharedKernel))
            .Where(r => r != "Ardea.SharedKernel")
            .ToList();

        Assert.True(violations.Count == 0, $"Building blocks must not reference: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Provider_sdk_knows_nothing_but_the_shared_kernel()
    {
        var violations = Assemblies.ArdeaReferences(Assemblies.SourcesAbstractions)
            .Where(r => r != "Ardea.SharedKernel")
            .ToList();

        Assert.Empty(violations);
    }
}
