namespace Ardea.SharedKernel;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    Unavailable,
}

public sealed record Error(string Code, string Message, ErrorKind Kind = ErrorKind.Validation)
{
    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);
}
