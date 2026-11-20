using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ardea.Catalog.Contracts;
using Ardea.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ardea.Catalog.Learning;

internal static class CatalogExport
{
    private const string NoVendor = "unknown-vendor";

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task<byte[]> WriteAsync(CatalogDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var products = await db.Products.AsNoTracking()
            .Where(p => p.Origin == ProductRow.Learned && p.State == ProductRow.Active)
            .OrderBy(p => p.Key).ToListAsync(cancellationToken);
        var rows = await db.UnknownProducts.AsNoTracking()
            .Where(u => u.Outcome == UnknownProductRow.Alias || u.Outcome == UnknownProductRow.New)
            .OrderBy(u => u.RawKey).ToListAsync(cancellationToken);
        var exported = products.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        var curated = (await db.Products.AsNoTracking().Where(p => p.Origin == ProductRow.Curated).Select(p => p.Key).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var group in products.GroupBy(p => FileName(p.Vendor), StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var entry = zip.CreateEntry($"{group.Key}.learned.json", CompressionLevel.Optimal);
                entry.LastWriteTime = now;
                await using var stream = entry.Open();
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                await writer.WriteAsync(VendorFile(group.First().Vendor, [.. group], rows, exported, curated, now));
            }

            var readme = zip.CreateEntry("README.txt", CompressionLevel.Optimal);
            readme.LastWriteTime = now;
            await using (var stream = readme.Open())
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteAsync(Readme(products.Count, rows, exported, now));
            }
        }

        return buffer.ToArray();
    }

    public static string VendorFile(string? vendor, IReadOnlyList<ProductRow> products, IReadOnlyList<UnknownProductRow> rows, ISet<string> exported, ISet<string> curated, DateTimeOffset now)
    {
        var known = new HashSet<string>(exported.Concat(curated), StringComparer.Ordinal);
        var text = new StringBuilder();
        text.AppendLine($"// Learned in an Ardea instance and exported on {now:yyyy-MM-dd} (master plan 6.9). Review every entry before it");
        text.AppendLine("// joins data/catalog/: names, kinds and version schemes come from the sources, by rule or by a language model.");
        if (vendor is null)
        {
            text.AppendLine("// The sources name no vendor for these products: set the vendor before the file joins the catalogue.");
        }

        text.AppendLine("{");
        text.AppendLine("  \"$schema\": \"../catalog.schema.json\",");
        text.AppendLine($"  \"vendor\": {Str(vendor ?? "TODO")},");
        text.AppendLine("  \"products\": [");
        for (var i = 0; i < products.Count; i++)
        {
            var p = products[i];
            var sources = rows.Where(r => r.ProductKey == p.Key).ToList();
            text.AppendLine("    {");
            text.AppendLine($"      // {Comment($"Learned by {p.LearnedBy ?? "rule"} ({p.Confidence ?? "high"}) from {string.Join(", ", sources.Select(s => $"'{s.RawKey}' ({s.RecordCount} records)"))}.")}");
            if (p.Reasoning is { Length: > 0 } reasoning)
            {
                text.AppendLine($"      // {Comment(reasoning)}");
            }

            text.AppendLine($"      \"key\": {Str(p.Key)},");
            text.AppendLine($"      \"name\": {Str(p.Name)},");
            text.AppendLine($"      \"kind\": {Str(p.Kind)},");
            text.Append($"      \"versionScheme\": {Str(p.VersionScheme)}");
            List(text, "cpe", p.CpePairs);
            List(text, "packages", p.Purls);
            var aliases = JsonSerializer.Deserialize<List<ProductAlias>>(p.Aliases, LearnedProposal.Json) ?? [];
            if (aliases.Count > 0)
            {
                text.AppendLine(",");
                text.Append("      \"aliases\": [")
                    .Append(string.Join(", ", aliases.Select(a => a.Vendor is null
                        ? $"{{ \"names\": [{string.Join(", ", a.Names.Select(Str))}] }}"
                        : $"{{ \"vendor\": {Str(a.Vendor)}, \"names\": [{string.Join(", ", a.Names.Select(Str))}] }}")))
                    .Append(']');
            }

            if (p.PlatformKey is { } platform && known.Contains(platform))
            {
                text.AppendLine(",");
                text.Append($"      \"platform\": {Str(platform)}");
            }

            List(text, "related", [.. p.RelatedKeys.Where(known.Contains)]);
            text.AppendLine();
            text.AppendLine(i == products.Count - 1 ? "    }" : "    },");
        }

        text.AppendLine("  ]");
        text.AppendLine("}");
        return text.ToString();
    }

    private static string Readme(int products, IReadOnlyList<UnknownProductRow> rows, ISet<string> exported, DateTimeOffset now)
    {
        var text = new StringBuilder();
        text.AppendLine($"Learned catalogue entries of an Ardea instance, exported on {now:yyyy-MM-dd} (master plan 6.9).");
        text.AppendLine();
        text.AppendLine($"{products} new product(s) in the *.learned.json files: the format of data/catalog/ (ADR 030). Review them,");
        text.AppendLine("then move them into the vendor's file or add the file as it is, and open a pull request.");
        text.AppendLine();
        var aliases = rows.Where(r => r.Outcome == UnknownProductRow.Alias && r.ProductKey is { } key && !exported.Contains(key)).ToList();
        text.AppendLine($"{aliases.Count} learned spelling(s) of existing entries; add each to the aliases of the entry in its file:");
        foreach (var row in aliases)
        {
            var alias = ProductKey.NormalizeVendor(row.Vendor) is null
                ? $"{{ \"names\": [{Str(row.Product)}] }}"
                : $"{{ \"vendor\": {Str(row.Vendor!)}, \"names\": [{Str(row.Product)}] }}";
            text.AppendLine($"- {row.ProductKey}: {alias}   ({Comment(row.RawKey)}, {row.RecordCount} records)");
        }

        return text.ToString();
    }

    private static void List(StringBuilder text, string name, IReadOnlyList<string> values)
    {
        if (values.Count > 0)
        {
            text.AppendLine(",");
            text.Append($"      \"{name}\": [{string.Join(", ", values.Select(Str))}]");
        }
    }

    private static string Str(string value) => JsonSerializer.Serialize(value, Json);

    private static string Comment(string text) => new([.. text.Select(c => char.IsControl(c) ? ' ' : c)]);

    private static string FileName(string? vendor)
    {
        var name = new string([.. (vendor ?? string.Empty).ToLowerInvariant().Select(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? c : '-')]);
        name = string.Join('-', name.Split('-', StringSplitOptions.RemoveEmptyEntries));
        return name.Length == 0 ? NoVendor : name.Length <= 60 ? name : name[..60];
    }
}
