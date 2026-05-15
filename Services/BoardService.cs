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
        // Busca UM quadro específico pelo ID
        public async Task<Board> GetBoardByIdAsync(Guid boardId)
        {
            var response = await _supabase.From<Board>()
                                          .Where(b => b.Id == boardId)
                                          .Single();
            return response;
        }

        // Busca todas as tarefas daquele quadro
        public async Task<List<PulseTask>> GetTasksByBoardIdAsync(Guid boardId)
        {
            // O '.Order("position")' garante que os cards venham na ordem exata em que o usuário os arrastou da última vez
            var response = await _supabase.From<PulseTask>()
                                           .Where(t => t.BoardId == boardId)
                                           // A linha .Order("position"...) foi removida daqui!
                                           .Get();

            return response.Models;
        }
        // Retorna null se der sucesso, ou a mensagem do Supabase se der erro
        public async Task<string> UpdateTaskStatusAsync(Guid taskId, string newStatus)
        {
            try
            {
                await _supabase.From<PulseTask>()
                    .Where(t => t.Id == taskId)
                    .Set(t => t.Status, newStatus)
                    .Update();

                return null; // Null significa que não houve erro (Sucesso!)
            }
            catch (Exception ex)
            {
                // Se falhar, pegamos a fofoca inteira do banco de dados
                Console.WriteLine($"Erro Supabase: {ex.Message}");
                return ex.Message;
            }
        }
        // Cria uma nova tarefa dentro de um quadro e de uma coluna específica
        public async Task<bool> CreateTaskAsync(Guid boardId, string title, string description, string status, string priority, DateTime? dueDate)
        {
            try
            {
                var newTask = new PulseTask
                {
                    BoardId = boardId,
                    Title = title,
                    Description = description,
                    Status = status,
                    Priority = string.IsNullOrEmpty(priority) ? "Média" : priority,
                    DueDate = dueDate,
                    CreatedAt = DateTime.UtcNow
                };

                await _supabase.From<PulseTask>().Insert(newTask);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao criar tarefa: {ex.Message}");
                return false;
            }
        }
        // Atualiza os detalhes completos de uma tarefa
        public async Task<bool> UpdateTaskDetailsAsync(Guid taskId, string title, string description, string priority, DateTime? dueDate)
        {
            try
            {
                await _supabase.From<PulseTask>()
                    .Where(t => t.Id == taskId)
                    .Set(t => t.Title, title)
                    .Set(t => t.Description, description)
                    .Set(t => t.Priority, priority)
                    .Set(t => t.DueDate, dueDate)
                    .Update();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao atualizar tarefa: {ex.Message}");
                return false;
            }
        }

        // Remove uma tarefa definitivamente
        public async Task<bool> DeleteTaskAsync(Guid taskId)
        {
            try
            {
                await _supabase.From<PulseTask>().Where(t => t.Id == taskId).Delete();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao deletar tarefa: {ex.Message}");
                return false;
            }
        }
    }
}