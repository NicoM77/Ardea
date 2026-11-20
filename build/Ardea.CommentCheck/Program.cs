using System.Text;
using Ardea.CommentCheck;

var fix = args.Contains("--fix", StringComparer.Ordinal);
var rootIndex = Array.IndexOf(args, "--root");
var root = rootIndex >= 0 && rootIndex + 1 < args.Length
    ? Path.GetFullPath(args[rootIndex + 1])
    : Repository.FindRoot(Directory.GetCurrentDirectory());

var openApi = OpenApiTexts.Load(root);
var findings = new List<Finding>();
var checkedFiles = 0;
foreach (var path in Repository.Files(root))
{
    var kind = Scope.Classify(path);
    if (kind == FileKind.None)
    {
        continue;
    }

    var fullPath = Path.Combine(root, path);
    var text = File.ReadAllText(fullPath);
    var found = kind switch
    {
        FileKind.CSharp => CSharpComments.Find(path, text, openApi),
        FileKind.Shell => ShellComments.Find(path, text),
        _ => [],
    };
    checkedFiles++;
    if (found.Count == 0)
    {
        continue;
    }

    if (fix)
    {
        var bom = File.ReadAllBytes(fullPath).AsSpan().StartsWith(Encoding.UTF8.Preamble);
        File.WriteAllText(fullPath, TextRemoval.Apply(text, found.Select(f => f.Removal)), new UTF8Encoding(bom));
    }

    findings.AddRange(found);
}

foreach (var finding in findings.OrderBy(f => f.Path, StringComparer.Ordinal).ThenBy(f => f.Line))
{
    Console.WriteLine($"{finding.Path}:{finding.Line}: {finding.Message}");
}

if (findings.Count == 0)
{
    Console.WriteLine($"Comments: none outside the exceptions in {checkedFiles} files (C#, SQL in C#, shell).");
    return 0;
}

Console.WriteLine(fix
    ? $"Removed {findings.Count} comments in {findings.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count()} files. Run dotnet format, then review the diff."
    : $"{findings.Count} comments outside the exceptions (CONTRIBUTING.md, \"Comments\"). Remove them, or run with --fix.");
return fix ? 0 : 1;
