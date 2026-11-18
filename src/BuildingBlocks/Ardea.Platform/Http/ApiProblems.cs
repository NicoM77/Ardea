using Ardea.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace Ardea.Platform.Http;

/// <summary>
/// Problem details after RFC 9457 (master plan 10.1) with a stable <c>code</c> the web app maps to
/// a translated message. Codes are snake_case and part of the API contract.
/// </summary>
public static class ApiProblems
{
    public static IResult From(Error error) => Problem(error.Code, Status(error.Kind), error.Message);

    public static IResult Problem(string code, int status, string title, IDictionary<string, string[]>? errors = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        return errors is null
            ? Results.Problem(title: title, statusCode: status, extensions: extensions)
            : Results.ValidationProblem(errors, title: title, statusCode: status, extensions: extensions);
    }

    public static IResult Validation(string code, string title, IDictionary<string, string[]> errors) =>
        Problem(code, StatusCodes.Status400BadRequest, title, errors);

    public static int Status(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };
}
