using System.Text.Json.Serialization;

namespace PulseBoardMigration.Models
{
    public class User
    {
        // Usado no value do select nos modais (mapeia para o UUID do Supabase)
        [JsonPropertyName("id")]
        public string Id { get; set; }

        // Usado para mostrar o nome da pessoa nos modais
        [JsonPropertyName("name")]
        public string Name { get; set; }

        // Essencial para o sistema, mesmo que não apareça diretamente no select do modal
        [JsonPropertyName("email")]
        public string Email { get; set; }

        // Fundamental para manter a UI idêntica ao Next.js (mostra a foto do utilizador nos cards do Kanban)
        [JsonPropertyName("avatar_url")]
        public string? AvatarUrl { get; set; }

        // Opcional, dependendo se você usa regras de acesso (Admin, Member)
        [JsonPropertyName("role")]
        public string? Role { get; set; }
    }
}