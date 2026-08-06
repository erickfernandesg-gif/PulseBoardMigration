namespace PulseBoardMigration.Services;

public class AuthService
{
    private readonly SupabaseClientFactory _clientFactory;

    public AuthService(SupabaseClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public async Task<Supabase.Gotrue.Session?> LoginAsync(string email, string password)
    {
        var client = _clientFactory.CreateAnonymousClient();
        return await client.Auth.SignIn(email, password);
    }

    public async Task LogoutAsync()
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Auth.SignOut();
    }
}
