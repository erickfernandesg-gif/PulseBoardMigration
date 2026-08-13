namespace PulseBoardMigration.Services;

public sealed record AuthenticatedLogin(
    Supabase.Gotrue.Session Session,
    PulseBoardMigration.Models.Profile Profile);

public class AuthService
{
    private readonly SupabaseClientFactory _clientFactory;

    public AuthService(SupabaseClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public async Task<AuthenticatedLogin?> LoginAsync(string email, string password)
    {
        var client = _clientFactory.CreateAnonymousClient();
        var session = await client.Auth.SignIn(email, password);
        if (session?.User?.Id == null || !Guid.TryParse(session.User.Id, out var userId)) return null;
        var profile = await client.From<PulseBoardMigration.Models.Profile>()
            .Where(item => item.Id == userId)
            .Single();
        if (profile == null || !profile.IsActive)
        {
            await client.Auth.SignOut();
            throw new UnauthorizedAccessException("Este usuário está desativado.");
        }
        return new AuthenticatedLogin(session, profile);
    }

    public async Task LogoutAsync()
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Auth.SignOut();
    }
}
