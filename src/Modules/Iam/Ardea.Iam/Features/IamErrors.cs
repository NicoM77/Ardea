using Ardea.Platform.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace Ardea.Iam.Features;

/// <summary>Error codes of the Iam endpoints; the web app maps each to a message.</summary>
internal static class IamErrors
{
    public static IResult InvalidCredentials() =>
        ApiProblems.Problem("invalid_credentials", StatusCodes.Status401Unauthorized, "The e-mail address or the password is wrong, or there were too many attempts.");

    public static IResult InvalidSetupToken() =>
        ApiProblems.Problem("invalid_setup_token", StatusCodes.Status403Forbidden, "The setup token is not valid.");

    public static IResult SetupCompleted() =>
        ApiProblems.Problem("setup_completed", StatusCodes.Status409Conflict, "This instance is already set up.");

    public static IResult TwoFactorExpired() =>
        ApiProblems.Problem("two_factor_expired", StatusCodes.Status401Unauthorized, "The sign-in step has expired. Sign in again.");

    public static IResult InvalidCode() =>
        ApiProblems.Problem("invalid_code", StatusCodes.Status400BadRequest, "The code is not valid.");

    public static IResult InvalidInvitation() =>
        ApiProblems.Problem("invalid_invitation", StatusCodes.Status400BadRequest, "The invitation is not valid or has expired.");

    public static IResult RegistrationClosed() =>
        ApiProblems.Problem("registration_closed", StatusCodes.Status403Forbidden, "Registration is closed on this instance.");

    public static IResult EmailTaken() =>
        ApiProblems.Problem("email_taken", StatusCodes.Status409Conflict, "An account with this e-mail address exists already.");

    public static IResult InvalidToken() =>
        ApiProblems.Problem("invalid_token", StatusCodes.Status400BadRequest, "The link is not valid or has expired.");

    public static IResult Validation(Dictionary<string, string[]> errors) =>
        ApiProblems.Validation("validation_failed", "Some values are missing or not valid.", errors);

    /// <summary>Identity's password errors as field errors with the code <c>weak_password</c>.</summary>
    public static IResult WeakPassword(IdentityResult result, string field = "password") =>
        ApiProblems.Validation("weak_password", "The password does not meet the rules.", new Dictionary<string, string[]>
        {
            [field] = [.. result.Errors.Select(e => e.Description)],
        });
}

/// <summary>Small checks on request fields; collects every problem at once.</summary>
internal sealed class FieldErrors
{
    private readonly Dictionary<string, string[]> _errors = new(StringComparer.Ordinal);

    public bool Any => _errors.Count > 0;

    public Dictionary<string, string[]> Errors => _errors;

    public FieldErrors Required(string field, string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _errors[field] = [message];
        }

        return this;
    }

    public FieldErrors Email(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _errors[field] = ["Enter an e-mail address."];
        }
        else if (value.Length > 256 || !System.Net.Mail.MailAddress.TryCreate(value, out var parsed) || parsed.Address != value.Trim())
        {
            _errors[field] = ["Enter a valid e-mail address."];
        }

        return this;
    }

    public FieldErrors MaxLength(string field, string? value, int length)
    {
        if (value is not null && value.Length > length)
        {
            _errors[field] = [$"Use at most {length} characters."];
        }

        return this;
    }
}
