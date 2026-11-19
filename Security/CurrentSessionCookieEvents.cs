using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using PulseBoardMigration.Services;
using System.Security.Claims;

namespace PulseBoardMigration.Security;

public sealed class CurrentSessionCookieEvents : CookieAuthenticationEvents
{
    private readonly SupabaseClientFactory _clients;
    private readonly ILogger<CurrentSessionCookieEvents> _logger;

    public CurrentSessionCookieEvents(SupabaseClientFactory clients, ILogger<CurrentSessionCookieEvents> logger)
    {
        _clients = clients;
        _logger = logger;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        try
        {
            if (context.Principal == null) throw new UnauthorizedAccessException("Sessão inválida.");
            var previousAccess = context.Properties.GetTokenValue("access_token");
            var previousRefresh = context.Properties.GetTokenValue("refresh_token");
            var profile = await _clients.ValidateSessionAsync(context.Principal, context.Properties);
            var identity = new ClaimsIdentity(context.Principal.Claims, context.Scheme.Name);
            var changed = ReplaceClaim(identity, ClaimTypes.Role, profile.Role)
                | ReplaceClaim(identity, "team_id", profile.TeamId?.ToString() ?? string.Empty);
            context.ReplacePrincipal(new ClaimsPrincipal(identity));
            context.ShouldRenew = changed
                || previousAccess != context.Properties.GetTokenValue("access_token")
                || previousRefresh != context.Properties.GetTokenValue("refresh_token");
        }
        catch (UnauthorizedAccessException)
        {
            await RejectAsync(context);
        }
        catch (Supabase.Gotrue.Exceptions.GotrueException exception)
        {
            _logger.LogInformation(exception, "Sessão do Supabase recusada; novo login necessário.");
            await RejectAsync(context);
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }

    private static bool ReplaceClaim(ClaimsIdentity identity, string type, string value)
    {
        if (identity.FindFirst(type)?.Value == value) return false;
        foreach (var claim in identity.FindAll(type).ToList()) identity.RemoveClaim(claim);
        identity.AddClaim(new Claim(type, value));
        return true;
    }
}
