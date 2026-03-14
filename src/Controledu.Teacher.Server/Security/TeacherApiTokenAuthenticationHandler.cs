using Controledu.Teacher.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace Controledu.Teacher.Server.Security;

internal sealed class TeacherApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ITeacherApiTokenProvider tokenProvider) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expectedToken = await tokenProvider.GetTokenAsync(Context.RequestAborted);
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            return AuthenticateResult.Fail("Teacher API token is not configured.");
        }

        var suppliedToken = ResolveSuppliedToken();
        if (string.IsNullOrWhiteSpace(suppliedToken))
        {
            return AuthenticateResult.NoResult();
        }

        if (!string.Equals(suppliedToken, expectedToken, StringComparison.Ordinal))
        {
            return AuthenticateResult.Fail("Invalid teacher API token.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "teacher-console"),
            new Claim(ClaimTypes.Name, "teacher-console"),
        };

        var identity = new ClaimsIdentity(claims, TeacherAuthDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, TeacherAuthDefaults.AuthenticationScheme);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.Append("WWW-Authenticate", $"Bearer realm=\"{TeacherAuthDefaults.AuthenticationScheme}\"");
        return Task.CompletedTask;
    }

    private string? ResolveSuppliedToken()
    {
        if (Request.Headers.TryGetValue(TeacherAuthDefaults.TokenHeaderName, out var headerValue))
        {
            var token = headerValue.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token.Trim();
            }
        }

        if (Request.Headers.Authorization.Count > 0)
        {
            var auth = Request.Headers.Authorization.ToString();
            const string bearerPrefix = "Bearer ";
            if (auth.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var bearer = auth[bearerPrefix.Length..].Trim();
                if (!string.IsNullOrWhiteSpace(bearer))
                {
                    return bearer;
                }
            }
        }

        if (Request.Query.TryGetValue("access_token", out var accessToken))
        {
            var token = accessToken.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token.Trim();
            }
        }

        return null;
    }
}
