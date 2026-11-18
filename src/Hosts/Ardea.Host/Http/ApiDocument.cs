using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ardea.Host.Http;

/// <summary>
/// The OpenAPI 3.1 document of the API (master plan 10.1). It is committed as
/// <c>openapi/ardea.v1.json</c>; a test fails when code and file drift apart, and the web client is
/// generated from the file.
/// </summary>
internal static class ApiDocument
{
    public const string Name = "v1";
    public const string Route = "/api/openapi/{documentName}.json";

    public static void Configure(OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "Ardea API",
                Version = "v1",
                Description = "Self-hosted early warning for security vulnerabilities. Errors are RFC 9457 problem details with a stable `code`.",
            };
            document.Servers?.Clear();
            return Task.CompletedTask;
        });

        // Route parameters a group filter reads, not the handler (the organisation of /orgs/{orgId}),
        // are still part of the path and must be declared for the generated client.
        options.AddOperationTransformer((operation, context, _) =>
        {
            var route = context.Description.RelativePath ?? string.Empty;
            foreach (var name in RouteParameters(route))
            {
                operation.Parameters ??= [];
                if (!operation.Parameters.Any(p => p.In == ParameterLocation.Path && string.Equals(p.Name, name, StringComparison.Ordinal)))
                {
                    operation.Parameters.Insert(0, new OpenApiParameter
                    {
                        Name = name,
                        In = ParameterLocation.Path,
                        Required = true,
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
                    });
                }
            }

            return Task.CompletedTask;
        });

        // Every problem carries the code the web app maps to a message.
        options.AddSchemaTransformer((schema, context, _) =>
        {
            var type = context.JsonTypeInfo.Type;
            if (type == typeof(Microsoft.AspNetCore.Mvc.ProblemDetails) || type == typeof(Microsoft.AspNetCore.Http.HttpValidationProblemDetails))
            {
                schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
                schema.Properties["code"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Stable error code, e.g. invalid_credentials." };
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>The names of the <c>{…}</c> segments of a route template that are UUIDs, e.g. <c>orgId</c>.</summary>
    private static IEnumerable<string> RouteParameters(string route) =>
        System.Text.RegularExpressions.Regex.Matches(route, @"\{(?<name>orgId)(:[^}]*)?\}", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100))
            .Select(m => m.Groups["name"].Value);

    /// <summary>Stable formatting for the committed file.</summary>
    public static string Normalize(string json) =>
        JsonNode.Parse(json)!.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
}
