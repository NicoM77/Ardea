using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Ardea.Architecture.Tests;

/// <summary>Types of a module are internal; only the module entry point is public (master plan 13.4).</summary>
public sealed class VisibilityTests
{
    private static readonly ArchUnitNET.Domain.Architecture Architecture =
        new ArchLoader().LoadAssemblies([.. Assemblies.Modules.Values]).Build();

    [Fact]
    public void Only_the_module_entry_point_is_public()
    {
        // EF Core generates migrations as public classes; they are generated code, not module API.
        var rule = Types().That().ArePublic().And().DoNotResideInNamespaceMatching(@"^Ardea\.\w+\.Persistence\.Migrations$")
            .Should().HaveNameEndingWith("Module")
            .AndShould().ImplementInterface(typeof(Platform.Modules.IModule))
            .Because("everything a module offers to others belongs in its Contracts assembly")
            .WithoutRequiringPositiveResults();

        rule.Check(Architecture);
    }
}
