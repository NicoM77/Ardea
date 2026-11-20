using Microsoft.Extensions.AI;

namespace Ardea.Assessment.Estimating;

internal static class EstimatePrompt
{
    public const string Version = "1";

    public static readonly string System = Load(Version);

    public static IReadOnlyList<ChatMessage> Messages(EstimateInput input) =>
    [
        new(ChatRole.System, System),
        new(ChatRole.User, $"Assess the vulnerability in the data block. Write every text in the language «{input.Language}».\n\n{input.ToDataBlock()}"),
    ];

    private static string Load(string version)
    {
        using var stream = typeof(EstimatePrompt).Assembly.GetManifestResourceStream($"Ardea.Assessment.Estimating.Prompts.estimate-{version}.md")
            ?? throw new InvalidOperationException($"The prompt estimate-{version}.md is missing from the module.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
