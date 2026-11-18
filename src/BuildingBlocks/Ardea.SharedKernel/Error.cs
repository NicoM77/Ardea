namespace Ardea.SharedKernel;

/// <summary>The kind of an expected failure; the API maps it to a status code.</summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    Unavailable,
}

/// <summary>An expected failure carried as a value up to the endpoint (master plan 3.5).</summary>
public sealed record Error(string Code, string Message, ErrorKind Kind = ErrorKind.Validation)
{
    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);
}
