using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ardea.Assessment.Estimating;

internal enum ModelVerdict
{
    Affected,
    Likely,
    Uncertain,
    Unlikely,
    NotAffected,
}

internal enum ModelGrade
{
    High,
    Medium,
    Low,
}

internal enum ConditionState
{
    Met,
    NotMet,
    Unknown,
}

internal sealed record EstimateAnswer(
    [property: Description("Exactly one verdict for every component of the data block.")] IReadOnlyList<ComponentVerdict> Verdicts,
    Likelihood Likelihood,
    [property: Description("How sure the assessment is.")] ModelGrade Confidence,
    [property: Description("One or two sentences, at most 240 characters.")] string WhatHappened,
    IReadOnlyList<Precondition> Preconditions,
    [property: Description("One sentence, at most 120 characters.")] string Impact,
    [property: Description("Ordered steps, at most five, each at most 140 characters.")] IReadOnlyList<ActionStep> Actions,
    IReadOnlyList<string> Unknowns,
    IReadOnlyList<ReasoningStep> Reasoning);

internal sealed record ComponentVerdict([property: Description("A component reference such as C1.")] string Component, ModelVerdict Verdict);

internal sealed record Likelihood(ModelGrade Level, [property: Description("One sentence.")] string Reason);

internal sealed record Precondition(string Condition, ConditionState State);

internal sealed record ActionStep(
    [property: Description("At most 140 characters.")] string Step,
    [property: Description("Numbers of the sources the step comes from.")] IReadOnlyList<int> Sources,
    [property: Description("True when the step is an own recommendation without a source.")] bool Own);

internal sealed record ReasoningStep(
    string Sentence,
    [property: Description("Numbers of the sources the sentence rests on.")] IReadOnlyList<int> Sources,
    [property: Description("References of the components the sentence rests on.")] IReadOnlyList<string> Components);

internal sealed record StoredEstimate(
    IReadOnlyList<StoredVerdict> Verdicts,
    Likelihood Likelihood,
    ModelGrade Confidence,
    string WhatHappened,
    IReadOnlyList<Precondition> Preconditions,
    string Impact,
    IReadOnlyList<ActionStep> Actions,
    IReadOnlyList<string> Unknowns,
    IReadOnlyList<StoredReasoning> Reasoning,
    IReadOnlyList<InputSource> Sources)
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    public JsonDocument ToDocument() => JsonSerializer.SerializeToDocument(this, Json);

    public static StoredEstimate? Read(JsonDocument output) =>
        output.RootElement.TryGetProperty("verdicts", out _) ? output.RootElement.Deserialize<StoredEstimate>(Json) : null;

    public static IReadOnlyDictionary<Guid, StoredVerdict> VerdictsOf(JsonDocument output) =>
        Read(output)?.Verdicts.ToDictionary(v => v.ComponentId) ?? new Dictionary<Guid, StoredVerdict>();

    public static string Name<T>(T value)
        where T : struct, Enum => JsonSerializer.Serialize(value, Json).Trim('"');
}

internal sealed record StoredVerdict(Guid ComponentId, ModelVerdict Verdict, string Context);

internal sealed record StoredReasoning(string Sentence, IReadOnlyList<int> Sources, IReadOnlyList<Guid> Components);

internal sealed record FailedEstimate(string Note, string Reason, int Attempts)
{
    public const string WithoutModel = "without_model";

    public JsonDocument ToDocument() => JsonSerializer.SerializeToDocument(this, StoredEstimate.Json);
}
