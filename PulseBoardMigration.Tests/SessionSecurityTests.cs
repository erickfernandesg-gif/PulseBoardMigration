using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PulseBoardMigration.Models;
using PulseBoardMigration.Security;
using PulseBoardMigration.Services;
using Xunit;

namespace PulseBoardMigration.Tests;

public class SessionSecurityTests
{
    [Theory]
    [InlineData("sb_publishable_test")]
    [InlineData("sb_secret_test")]
    public void OpaqueApiKeysAreNeverSentAsBearerTokens(string key)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Supabase:Url"] = "https://test.supabase.co", ["Supabase:AnonKey"] = key }).Build();
        var client = new SupabaseClientFactory(configuration, new HttpContextAccessor()).CreateAnonymousClient();
        Assert.False(client.Auth.GetHeaders!().ContainsKey("Authorization"));
        Assert.False(client.Postgrest.GetHeaders!().ContainsKey("Authorization"));
        Assert.False(client.Storage.GetHeaders!().ContainsKey("Authorization"));
        Assert.Equal(key, client.Postgrest.GetHeaders!()["apiKey"]);
    }

    [Fact]
    public async Task DemotionReplacesTheRoleBeforeAuthorization()
    {
        var context = Context();
        var factory = new SessionFactory(new Profile { Role = "user", IsActive = true });
        await Events(factory).ValidatePrincipal(context);
        Assert.False(context.Principal!.IsInRole("admin"));
        Assert.True(context.Principal.IsInRole("user"));
        Assert.True(context.ShouldRenew);
    }

    [Fact]
    public async Task RotatedTokensRenewTheAuthenticationCookie()
    {
        var context = Context();
        var factory = new SessionFactory(new Profile { Role = "admin", IsActive = true }) { Rotate = true };
        await Events(factory).ValidatePrincipal(context);
        Assert.Equal("new-refresh", context.Properties.GetTokenValue("refresh_token"));
        Assert.True(context.ShouldRenew);
    }

    [Fact]
    public async Task DisabledUserLosesTheCookie()
    {
        var context = Context();
        await Events(new SessionFactory(null)).ValidatePrincipal(context);
        Assert.Null(context.Principal);
        Assert.Contains(".AspNetCore.Cookies=", context.HttpContext.Response.Headers.SetCookie.ToString());
    }

    private static CurrentSessionCookieEvents Events(SessionFactory factory) =>
        new(factory, NullLogger<CurrentSessionCookieEvents>.Instance);

    private static CookieValidatePrincipalContext Context()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var properties = new AuthenticationProperties();
        properties.StoreTokens(new[]
        {
            new AuthenticationToken { Name = "access_token", Value = "old-access" },
            new AuthenticationToken { Name = "refresh_token", Value = "old-refresh" }
        });
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "admin")
        }, CookieAuthenticationDefaults.AuthenticationScheme));
        return new CookieValidatePrincipalContext(http,
            new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler)),
            new CookieAuthenticationOptions(), new AuthenticationTicket(principal, properties, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    private sealed class SessionFactory : SupabaseClientFactory
    {
        private readonly Profile? _profile;

        public SessionFactory(Profile? profile)
            : base(new ConfigurationBuilder().Build(), new HttpContextAccessor())
        {
            _profile = profile;
        }

        public bool Rotate { get; init; }
        public override Task<Profile> ValidateSessionAsync(ClaimsPrincipal principal, AuthenticationProperties properties)
        {
            if (_profile == null) throw new UnauthorizedAccessException();
            if (Rotate)
            {
                properties.UpdateTokenValue("access_token", "new-access");
                properties.UpdateTokenValue("refresh_token", "new-refresh");
            }
            return Task.FromResult(_profile);
        }
    }
}
