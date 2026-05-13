using Supabase;
using System.Collections.Generic;
using System.Threading.Tasks;
using PulseBoardMigration.Models;

namespace PulseBoardMigration.Services
{
    public class BoardService
    {
        private readonly Client _supabase;

        public BoardService(Client supabase)
        {
            _supabase = supabase;
        }

        // Método que busca a lista de quadros no banco
        public async Task<List<Board>> GetBoardsAsync()
        {
            // Isso equivale a: SELECT * FROM boards
            var response = await _supabase.From<Board>().Get();

            return response.Models;
        }
        public async Task<Board> CreateBoardAsync(string name, string description, Guid ownerId)
        {
            var newBoard = new Board
            {
                Name = name, // Mudou
                Description = description,
                OwnerId = ownerId,
                Status = "active", // Padrão
                CreatedAt = System.DateTime.UtcNow,
                // Cria as colunas padrão no formato JSON que o banco espera
                Settings = new System.Collections.Generic.List<BoardColumnSetting>
        {
            new BoardColumnSetting { Id = "todo", Title = "A Fazer" },
            new BoardColumnSetting { Id = "in-progress", Title = "Em Execução" },
            new BoardColumnSetting { Id = "done", Title = "Concluído" }
        }
            };

            var response = await _supabase.From<Board>().Insert(newBoard);
            return response.Models.FirstOrDefault();
        }
    }
}