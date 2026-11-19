using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ardea.Assessment.Estimating;

/// <summary>A verdict on affectedness (master plan 7.5, 4.6).</summary>
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

/// <summary>
/// The answer of the model exactly as master plan 7.5 fixes it, the type of <c>GetResponseAsync&lt;T&gt;</c>: its JSON
/// schema goes to providers that enforce one. Names are snake_case; unknown fields make the answer invalid.
/// </summary>
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

/// <summary>
/// The checked output as <c>assessment.output</c> stores it (master plan 4.6): the answer with real component ids in
/// place of the placeholders, and the numbered sources its numbers refer to.
/// </summary>
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
        // GetResponseAsync<T> makes the options read-only, which needs an explicit resolver.
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    public JsonDocument ToDocument() => JsonSerializer.SerializeToDocument(this, Json);

    public static StoredEstimate? Read(JsonDocument output) =>
        output.RootElement.TryGetProperty("verdicts", out _) ? output.RootElement.Deserialize<StoredEstimate>(Json) : null;

    /// <summary>The verdicts per component id as the rule engine takes them.</summary>
    public static IReadOnlyDictionary<Guid, string> VerdictsOf(JsonDocument output) =>
        Read(output)?.Verdicts.ToDictionary(v => v.ComponentId, v => Name(v.Verdict)) ?? new Dictionary<Guid, string>();

    public static string Name<T>(T value)
        where T : struct, Enum => JsonSerializer.Serialize(value, Json).Trim('"');
}

internal sealed record StoredVerdict(Guid ComponentId, ModelVerdict Verdict);

internal sealed record StoredReasoning(string Sentence, IReadOnlyList<int> Sources, IReadOnlyList<Guid> Components);

/// <summary>A failed attempt: no text of the model, only why (master plan 7.5 «ohne KI-Einschätzung»).</summary>
internal sealed record FailedEstimate(string Note, string Reason, int Attempts)
{
    public const string WithoutModel = "without_model";

    public JsonDocument ToDocument() => JsonSerializer.SerializeToDocument(this, StoredEstimate.Json);
}
