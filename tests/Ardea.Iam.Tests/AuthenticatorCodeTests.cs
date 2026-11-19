using Ardea.Iam.Security;

namespace Ardea.Iam.Tests;

/// <summary>RFC 6238 codes and the time step they belong to, without a database (issue #85).</summary>
public sealed class AuthenticatorCodeTests
{
    // RFC 6238 appendix B: the ASCII key "12345678901234567890" in base32.
    private const string RfcKey = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 10, TimeSpan.Zero);

    [Theory]
    [InlineData(59, "287082", 1L)]
    [InlineData(1111111109, "081804", 37037036L)]
    [InlineData(1234567890, "005924", 41152263L)]
    public void A_code_of_the_rfc_test_vectors_matches_its_time_step(long unixSeconds, string code, long step)
    {
        Assert.Equal(step, AuthenticatorCode.MatchingStep(RfcKey, code, DateTimeOffset.FromUnixTimeSeconds(unixSeconds)));
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_code_matches_its_own_time_step_anywhere_in_the_drift_window(int offset)
    {
        var code = Totp.Code(RfcKey, Now.AddSeconds(offset * AuthenticatorCode.StepSeconds));

        Assert.Equal(AuthenticatorCode.StepAt(Now) + offset, AuthenticatorCode.MatchingStep(RfcKey, code, Now));
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(3)]
    public void A_code_outside_the_drift_window_matches_no_step(int offset)
    {
        var code = Totp.Code(RfcKey, Now.AddSeconds(offset * AuthenticatorCode.StepSeconds));

        Assert.Null(AuthenticatorCode.MatchingStep(RfcKey, code, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("+12345")]
    [InlineData(" 12345")]
    [InlineData("12a456")]
    public void A_malformed_code_matches_nothing(string? code)
    {
        Assert.Null(AuthenticatorCode.MatchingStep(RfcKey, code, Now));
    }

    [Fact]
    public void A_key_that_is_not_base32_matches_nothing()
    {
        Assert.Null(AuthenticatorCode.MatchingStep("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJ1", Totp.Code(RfcKey, Now), Now));
    }
}
