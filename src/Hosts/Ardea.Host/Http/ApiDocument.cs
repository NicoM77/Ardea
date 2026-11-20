using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ardea.Host.Http;

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

    private static IEnumerable<string> RouteParameters(string route) =>
        System.Text.RegularExpressions.Regex.Matches(route, @"\{(?<name>orgId)(:[^}]*)?\}", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1))
            .Select(m => m.Groups["name"].Value);

    public static string Normalize(string json)
    {
        var node = JsonNode.Parse(json)!;
        NormalizeStrings(node);
        return node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
    }

    private static void NormalizeStrings(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj.ToList())
                {
                    if (child is JsonValue value && value.TryGetValue<string>(out var text) && text.Contains('\r', StringComparison.Ordinal))
                    {
                        obj[key] = text.ReplaceLineEndings("\n");
                    }
                    else
                    {
                        NormalizeStrings(child);
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue value && value.TryGetValue<string>(out var text) && text.Contains('\r', StringComparison.Ordinal))
                    {
                        array[i] = text.ReplaceLineEndings("\n");
                    }
                    else
                    {
                        NormalizeStrings(array[i]);
                    }
                }

                break;
        }
    }
}
