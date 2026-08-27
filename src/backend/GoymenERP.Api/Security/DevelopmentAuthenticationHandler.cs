using System.Security.Claims;
using System.Text.Encodings.Web;
using GoymenERP.BuildingBlocks.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Security;

public sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<BootstrapIdentityOptions> identityOptions,
    IHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, logger, encoder)
{
    public const string SchemeName = "DevelopmentBootstrap";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            return Task.FromResult(AuthenticateResult.Fail(
                "Gerçek kimlik sağlayıcısı henüz yapılandırılmadı."));
        }

        var options = identityOptions.Value;
        if (!Request.Headers.TryGetValue(options.HeaderName, out var headerValue))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var selectedUser = options.Users.SingleOrDefault(x =>
            string.Equals(x.UserId, headerValue.ToString(), StringComparison.Ordinal));
        if (selectedUser is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Geliştirme kullanıcısı geçersiz."));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, selectedUser.UserId),
            new(ClaimTypes.Name, selectedUser.DisplayName),
            new(GoymenClaimTypes.IdentityIssuer, BootstrapIdentityOptions.Issuer),
            new(GoymenClaimTypes.IdentitySubject, selectedUser.UserId),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
