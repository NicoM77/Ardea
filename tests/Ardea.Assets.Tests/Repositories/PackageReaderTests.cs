using System.Text;
using Ardea.Assets.Repositories;
using Ardea.Assets.Repositories.Packages;
using Ardea.Catalog.Contracts;

namespace Ardea.Assets.Tests.Repositories;

public sealed class PackageReaderTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static PackageFinding Find(IEnumerable<PackageFinding> findings, string type, string name, string? version = null) =>
        Assert.Single(findings, f => f.Package == new PackageCoordinate(type, name) && (version is null || f.Version == version));

    [Fact]
    public void Package_lock_v3_of_the_web_client_gives_exact_versions_dev_flags_and_direct_packages()
    {
        var result = NpmLockReader.Read(Fixture("web-package-lock.json"), "web/package-lock.json");

        Assert.True(result.Ok, result.Error);
        // 835 install paths, 763 distinct package and version.
        Assert.Equal(763, result.Findings.Count);
        Assert.All(result.Findings, f => Assert.True(f.Exact && f.Version is not null));
        Assert.Equal(720, result.Findings.Count(f => f.Scope == DependencyScope.Development));
        Assert.Equal(43, result.Findings.Count(f => f.Scope == DependencyScope.Runtime));
        // 16 dependencies and 15 devDependencies of the root; typescript is declared as dev but not flagged dev by npm.
        Assert.Equal(31, result.Findings.Count(f => f.Relation == DependencyRelation.Direct));
        Assert.Equal(14, result.Findings.Count(f => f.Relation == DependencyRelation.Direct && f.Scope == DependencyScope.Development));
        Assert.DoesNotContain(result.Findings, f => f.Relation == DependencyRelation.Unknown);

        var core = Find(result.Findings, "npm", "@angular/core");
        Assert.Equal(("22.2.6", DependencyScope.Runtime, DependencyRelation.Direct), (core.Version, core.Scope, core.Relation));
        Assert.Equal(new FindingEvidence(FindingSource.Lockfile, "web/package-lock.json", 500), core.Evidence);
        Assert.Equal("pkg:npm/%40angular/core@22.2.6", core.Purl);

        // Only under node_modules of another package: transitive.
        var nested = Find(result.Findings, "npm", "@jridgewell/gen-mapping", "0.3.13");
        Assert.Equal(DependencyRelation.Transitive, nested.Relation);
    }

    [Fact]
    public void Package_lock_with_workspaces_links_and_aliases()
    {
        var json = """
            {
              "name": "mono", "lockfileVersion": 3,
              "packages": {
                "": { "name": "mono", "workspaces": ["packages/*"], "devDependencies": { "jest": "^29.0.0" } },
                "packages/app": { "name": "app", "dependencies": { "lodash": "^4.17.0", "old": "npm:legacy@1.0.0" } },
                "node_modules/app": { "resolved": "packages/app", "link": true },
                "node_modules/lodash": { "version": "4.17.21" },
                "node_modules/old": { "name": "legacy", "version": "1.0.0" },
                "node_modules/jest": { "version": "29.7.0", "dev": true },
                "node_modules/jest/node_modules/lodash": { "version": "4.17.20", "dev": true },
                "node_modules/local": { "version": "0.0.1", "resolved": "file:../local" },
                "node_modules/chalk": { "version": "5.3.0", "devOptional": true }
              }
            }
            """;

        var result = NpmLockReader.Read(Encoding.UTF8.GetBytes(json), "package-lock.json");

        Assert.True(result.Ok, result.Error);
        Assert.Equal(5, result.Findings.Count);
        Assert.Equal(DependencyRelation.Direct, Find(result.Findings, "npm", "lodash", "4.17.21").Relation);
        Assert.Equal(DependencyRelation.Direct, Find(result.Findings, "npm", "legacy").Relation);
        Assert.Equal((DependencyScope.Development, DependencyRelation.Direct), (Find(result.Findings, "npm", "jest").Scope, Find(result.Findings, "npm", "jest").Relation));
        Assert.Equal(DependencyRelation.Transitive, Find(result.Findings, "npm", "lodash", "4.17.20").Relation);
        Assert.Equal((DependencyScope.Runtime, DependencyRelation.Transitive), (Find(result.Findings, "npm", "chalk").Scope, Find(result.Findings, "npm", "chalk").Relation));
        Assert.DoesNotContain(result.Findings, f => f.Package.Name is "app" or "local");
    }

    [Fact]
    public void Package_lock_v1_marks_nested_packages_transitive_and_leaves_the_top_level_open()
    {
        var json = """
            {
              "name": "old", "lockfileVersion": 1, "requires": true,
              "dependencies": {
                "express": { "version": "4.17.1", "requires": { "debug": "2.6.9" },
                  "dependencies": { "debug": { "version": "2.6.9" } } },
                "mocha": { "version": "8.0.0", "dev": true },
                "fork": { "version": "git+https://github.com/x/fork.git#abc" }
              }
            }
            """;

        var result = NpmLockReader.Read(Encoding.UTF8.GetBytes(json), "package-lock.json");

        Assert.True(result.Ok, result.Error);
        Assert.Equal(DependencyRelation.Unknown, Find(result.Findings, "npm", "express").Relation);
        Assert.Equal(DependencyRelation.Transitive, Find(result.Findings, "npm", "debug").Relation);
        Assert.Equal(DependencyScope.Development, Find(result.Findings, "npm", "mocha").Scope);
        var fork = Find(result.Findings, "npm", "fork");
        Assert.Equal((null, false), (fork.Version, fork.Exact));
    }

    [Theory]
    [InlineData("{ \"packages\": ")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{ \"name\": \"no packages\" }")]
    [InlineData("not json at all")]
    public void Malformed_lockfiles_are_a_result_not_a_crash(string json)
    {
        var npm = NpmLockReader.Read(Encoding.UTF8.GetBytes(json), "package-lock.json");
        var nuget = NuGetLockReader.Read(Encoding.UTF8.GetBytes(json), "packages.lock.json");

        Assert.False(npm.Ok);
        Assert.Empty(npm.Findings);
        Assert.False(nuget.Ok);
    }

    [Fact]
    public void Nesting_beyond_the_maximum_depth_is_refused()
    {
        var json = string.Concat(Enumerable.Repeat("{\"a\":", 200)) + "1" + new string('}', 200);

        Assert.False(NpmLockReader.Read(Encoding.UTF8.GetBytes(json), "package-lock.json").Ok);
    }

    [Fact]
    public void Packages_lock_of_the_host_gives_resolved_versions_without_projects()
    {
        var result = NuGetLockReader.Read(Fixture("host-packages.lock.json"), "src/Hosts/Ardea.Host/packages.lock.json");

        Assert.True(result.Ok, result.Error);
        Assert.Equal(37, result.Findings.Count);
        Assert.All(result.Findings, f => Assert.True(f.Exact && f.Package.Type == "nuget" && f.Scope == DependencyScope.Runtime));
        Assert.Equal(["mailkit", "microsoft.aspnetcore.openapi"], result.Findings.Where(f => f.Relation == DependencyRelation.Direct).Select(f => f.Package.Name));
        Assert.Equal(35, result.Findings.Count(f => f.Relation == DependencyRelation.Transitive));
        Assert.DoesNotContain(result.Findings, f => f.Package.Name.StartsWith("ardea.", StringComparison.Ordinal));

        var mailKit = Find(result.Findings, "nuget", "mailkit");
        Assert.Equal(("5.0.0", 5), (mailKit.Version, mailKit.Evidence.Line));
        Assert.Equal("10.0.3", Find(result.Findings, "nuget", "npgsql").Version);
    }

    [Fact]
    public void Packages_lock_deduplicates_target_frameworks_and_runtimes()
    {
        var json = """
            {
              "version": 2,
              "dependencies": {
                "net8.0": {
                  "Serilog": { "type": "Transitive", "resolved": "3.1.1" },
                  "Polly": { "type": "Direct", "requested": "[8.0.0, )", "resolved": "8.0.0" }
                },
                "net10.0": {
                  "Serilog": { "type": "Direct", "requested": "[3.1.1, )", "resolved": "3.1.1" },
                  "Polly": { "type": "CentralTransitive", "requested": "[8.4.0, )", "resolved": "8.4.0" }
                },
                "net10.0/linux-x64": {
                  "Serilog": { "type": "Direct", "requested": "[3.1.1, )", "resolved": "3.1.1" }
                }
              }
            }
            """;

        var result = NuGetLockReader.Read(Encoding.UTF8.GetBytes(json), "tests/Shop.Tests/packages.lock.json");

        Assert.Equal(3, result.Findings.Count);
        Assert.Equal(DependencyRelation.Direct, Find(result.Findings, "nuget", "serilog").Relation);
        Assert.Equal(DependencyRelation.Direct, Find(result.Findings, "nuget", "polly", "8.0.0").Relation);
        Assert.Equal(DependencyRelation.Transitive, Find(result.Findings, "nuget", "polly", "8.4.0").Relation);
        Assert.All(result.Findings, f => Assert.Equal(DependencyScope.Development, f.Scope));
    }

    [Theory]
    [InlineData("tests/Ardea.TestSupport/packages.lock.json", true)]
    [InlineData("src/Shop.Tests/packages.lock.json", true)]
    [InlineData("Shop.Test/packages.lock.json", true)]
    [InlineData("src/Shop.UnitTests/packages.lock.json", true)]
    [InlineData("src/Shop.IntegrationTest/packages.lock.json", true)]
    [InlineData("test/packages.lock.json", true)]
    [InlineData("src/Hosts/Ardea.Host/packages.lock.json", false)]
    [InlineData("packages.lock.json", false)]
    [InlineData("src/Shop.Contest/packages.lock.json", false)]
    [InlineData("src/Testing.Tools/packages.lock.json", false)]
    public void Test_projects_are_recognised_by_their_folders(string path, bool test) =>
        Assert.Equal(test, NuGetLockReader.IsTestProject(path));

    [Fact]
    public async Task Sbom_without_lockfile_has_ranges_and_no_relation()
    {
        await using var stream = new MemoryStream(Fixture("express-sbom.json"));

        var result = await SpdxReader.ReadAsync(stream, TestContext.Current.CancellationToken);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("master", result.DescribedBranch);
        // 54 packages: the repository itself (skipped), 44 npm packages, 9 GitHub Actions.
        Assert.Equal(53, result.Findings.Count);
        Assert.Equal(44, result.Findings.Count(f => f.Package.Type == "npm"));
        Assert.Equal(9, result.Findings.Count(f => f.Package.Type == "githubactions"));
        Assert.DoesNotContain(result.Findings, f => f.Package.Type == "github");
        Assert.All(result.Findings, f => Assert.Equal((DependencyScope.Unknown, DependencyRelation.Unknown, FindingSource.Sbom), (f.Scope, f.Relation, f.Evidence.Source)));

        // From package.json only: a range is "version unknown", a pinned version stays.
        Assert.Equal(8, result.Findings.Count(f => f.Package.Type == "npm" && f.Version is not null));
        Assert.Equal((null, false), (Find(result.Findings, "npm", "fresh").Version, Find(result.Findings, "npm", "fresh").Exact));
        Assert.Null(Find(result.Findings, "npm", "vhost").Version);
        Assert.Equal(("2.1.1", true), (Find(result.Findings, "npm", "cookie-session").Version, Find(result.Findings, "npm", "cookie-session").Exact));
        Assert.Equal("3d3c42e5aac5ba805825da76410c181273ba90b1", Find(result.Findings, "githubactions", "actions/checkout").Version);
    }

    [Fact]
    public async Task Sbom_with_lockfile_paths_separates_direct_from_transitive()
    {
        await using var stream = new MemoryStream(Fixture("checkout-sbom.json"));

        var result = await SpdxReader.ReadAsync(stream, TestContext.Current.CancellationToken);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("main", result.DescribedBranch);
        var npm = result.Findings.Where(f => f.Package.Type == "npm").ToList();
        Assert.Equal(117, npm.Count);
        Assert.Equal(21, npm.Count(f => f.Relation == DependencyRelation.Direct));
        Assert.Equal(96, npm.Count(f => f.Relation == DependencyRelation.Transitive));
        Assert.All(npm, f => Assert.True(f.Exact));
        Assert.Equal(DependencyRelation.Direct, Find(npm, "npm", "@actions/core").Relation);
        // Actions hang off the repository without paths of their own.
        Assert.All(result.Findings.Where(f => f.Package.Type == "githubactions"), f => Assert.Equal(DependencyRelation.Unknown, f.Relation));
    }

    [Fact]
    public async Task Sbom_nuget_versions_from_project_files_are_never_exact()
    {
        var json = """
            {
              "spdxVersion": "SPDX-2.3", "SPDXID": "SPDXRef-DOCUMENT",
              "packages": [
                { "SPDXID": "SPDXRef-root", "name": "com.github.o/r", "versionInfo": "main",
                  "externalRefs": [{ "referenceCategory": "PACKAGE-MANAGER", "referenceType": "purl", "referenceLocator": "pkg:github/o/r@main" }] },
                { "SPDXID": "SPDXRef-a", "name": "Serilog", "versionInfo": ">= 0",
                  "externalRefs": [{ "referenceCategory": "PACKAGE-MANAGER", "referenceType": "purl", "referenceLocator": "pkg:nuget/Serilog@%3E%3D%200" }] },
                { "SPDXID": "SPDXRef-b", "name": "Polly", "versionInfo": "8.4.0",
                  "externalRefs": [{ "referenceCategory": "PACKAGE-MANAGER", "referenceType": "purl", "referenceLocator": "pkg:nuget/Polly@8.4.0" }] },
                { "SPDXID": "SPDXRef-c", "name": "no-purl", "versionInfo": "1.0.0" },
                { "SPDXID": "SPDXRef-d", "name": "requests", "versionInfo": "2.31.0",
                  "externalRefs": [{ "referenceCategory": "PACKAGE-MANAGER", "referenceType": "purl", "referenceLocator": "pkg:pypi/Requests" }] }
              ],
              "relationships": [
                { "spdxElementId": "SPDXRef-DOCUMENT", "relatedSpdxElement": "SPDXRef-root", "relationshipType": "DESCRIBES" },
                { "spdxElementId": "SPDXRef-root", "relatedSpdxElement": "SPDXRef-a", "relationshipType": "DEPENDS_ON" }
              ]
            }
            """;

        var result = await SpdxReader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)), TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Findings.Count);
        Assert.Equal((null, false), (Find(result.Findings, "nuget", "serilog").Version, Find(result.Findings, "nuget", "serilog").Exact));
        Assert.Equal(("8.4.0", false), (Find(result.Findings, "nuget", "polly").Version, Find(result.Findings, "nuget", "polly").Exact));
        // Without a version in the purl, versionInfo counts.
        Assert.Equal(("2.31.0", true), (Find(result.Findings, "pypi", "requests").Version, Find(result.Findings, "pypi", "requests").Exact));
    }

    [Theory]
    [InlineData("{ \"packages\": [] }")]
    [InlineData("{ \"spdxVersion\": \"SPDX-3.0\" }")]
    [InlineData("{ \"spdxVersion\": \"SPDX-2.3\", \"packages\": [ ")]
    [InlineData("")]
    public async Task Malformed_sboms_are_a_result_not_a_crash(string json)
    {
        var result = await SpdxReader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(json)), TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task Lockfile_findings_win_over_the_sbom_for_the_same_package()
    {
        await using var stream = new MemoryStream(Fixture("express-sbom.json"));
        var sbom = (await SpdxReader.ReadAsync(stream, TestContext.Current.CancellationToken)).Findings;
        var lockfile = NpmLockReader.Read(Encoding.UTF8.GetBytes("""
            {
              "lockfileVersion": 3,
              "packages": {
                "": { "dependencies": { "fresh": "^2.0.0" }, "devDependencies": { "mocha": "^11.7.5" } },
                "node_modules/fresh": { "version": "2.0.0" },
                "node_modules/mocha": { "version": "11.7.5", "dev": true },
                "node_modules/mocha/node_modules/debug": { "version": "4.3.7", "dev": true },
                "node_modules/debug": { "version": "4.4.1" }
              }
            }
            """), "package-lock.json").Findings;

        var merged = PackageMerge.Merge(sbom, lockfile);

        Assert.Equal(53 + 1, merged.Count);
        var fresh = Find(merged, "npm", "fresh");
        Assert.Equal(("2.0.0", true, DependencyScope.Runtime, DependencyRelation.Direct, FindingSource.Lockfile), (fresh.Version, fresh.Exact, fresh.Scope, fresh.Relation, fresh.Evidence.Source));
        Assert.Equal(DependencyScope.Development, Find(merged, "npm", "mocha").Scope);
        // The SBOM's range for debug is gone; both versions from the lockfile are there.
        Assert.Equal(["4.3.7", "4.4.1"], merged.Where(f => f.Package.Name == "debug").Select(f => f.Version));
        var vhost = Find(merged, "npm", "vhost");
        Assert.Equal((DependencyScope.Unknown, DependencyRelation.Unknown, FindingSource.Sbom), (vhost.Scope, vhost.Relation, vhost.Evidence.Source));
    }

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("v1.2.3", true)]
    [InlineData("1.0.0-beta.1+build.5", true)]
    [InlineData("3d3c42e5aac5ba805825da76410c181273ba90b1", true)]
    [InlineData("^2.0.0", false)]
    [InlineData("~3.0.2", false)]
    [InlineData(">= 0", false)]
    [InlineData(">=1.0.0", false)]
    [InlineData("*", false)]
    [InlineData("1.x", false)]
    [InlineData("1.2.X", false)]
    [InlineData("1.0.0 - 2.0.0", false)]
    [InlineData("[1.0.0, )", false)]
    [InlineData("latest", false)]
    [InlineData("NOASSERTION", false)]
    [InlineData("git+https://github.com/x/y.git#abc", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Ranges_are_no_version(string? version, bool plain) =>
        Assert.Equal(plain, PackageReaders.IsPlainVersion(version));

    [Theory]
    [InlineData("src/app/package.json", "src/app", true)]
    [InlineData("src/app", "src/app", true)]
    [InlineData("src/application/package.json", "src/app", false)]
    [InlineData("SRC/app/package.json", "src/app", false)]
    [InlineData("anything", null, true)]
    public void Subfolders_are_compared_not_resolved(string path, string? folder, bool within) =>
        Assert.Equal(within, RepositoryPaths.IsWithin(path, folder));

    [Theory]
    [InlineData("package.json", true)]
    [InlineData("src/a b/package.json", true)]
    [InlineData("../secret", false)]
    [InlineData("src/../../secret", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("src//x", false)]
    [InlineData("src\\x", false)]
    [InlineData("./x", false)]
    [InlineData("", false)]
    public void Paths_with_traversal_are_refused(string path, bool valid) =>
        Assert.Equal(valid, RepositoryPaths.IsValidPath(path));
}
