using Supabase;
using System.Threading.Tasks;

namespace PulseBoardMigration.Services
{
    public class AuthService
    {
        private readonly Client _supabase;

        public AuthService(Client supabase)
        {
            _supabase = supabase;
        }

        // Método para fazer o Login
        public async Task<Supabase.Gotrue.Session> LoginAsync(string email, string password)
        {
            // O Supabase verifica as credenciais e devolve uma sessão (com o Token)
            var session = await _supabase.Auth.SignIn(email, password);
            return session;
        }

        // Método para fazer Logout
        public async Task LogoutAsync()
        {
            await _supabase.Auth.SignOut();
        }
    }
}