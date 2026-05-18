using Supabase;
using System;
using System.Collections.Generic;
using System.Linq;
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
            var response = await _supabase.From<PulseTask>()
                                          .Where(t => t.BoardId == boardId)
                                          .Get();

            return response.Models;
        }

        // Atualizado: Retorna bool para facilitar a validação no Controller AJAX (Drag and Drop)
        public async Task<bool> UpdateTaskStatusAsync(Guid taskId, string newStatus)
        {
            try
            {
                var response = await _supabase.From<PulseTask>()
                    .Where(t => t.Id == taskId)
                    .Set(t => t.Status, newStatus)
                    .Update();

                // Retorna true se houver modelos alterados (Sucesso!)
                return response.Models.Any();
            }
            catch (Exception ex)
            {
                // Se falhar, pegamos a fofoca inteira do banco de dados
                Console.WriteLine($"Erro Supabase: {ex.Message}");
                return false;
            }
        }

        // Atualizado: Adicionados os novos campos avançados
        public async Task<PulseTask> CreateTaskAsync(Guid boardId, string title, string description, string status, string priority, DateTime? startDate, DateTime? dueDate, Guid? assigneeId, string department, string riskLevel, int? storyPoints, string tags)
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
                    StartDate = startDate,
                    DueDate = dueDate,
                    AssigneeId = assigneeId,
                    Department = department, // Novo
                    RiskLevel = riskLevel,   // Novo
                    StoryPoints = storyPoints, // Novo
                    Tags = tags,             // Novo
                    CreatedAt = DateTime.UtcNow
                };

                var response = await _supabase.From<PulseTask>().Insert(newTask);

                // Retorna a tarefa com o ID gerado pelo banco para usarmos no JS
                return response.Models.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao criar tarefa: {ex.Message}");
                return null;
            }
        }

        // Atualizado: Adicionados os novos campos avançados
        public async Task<PulseTask> UpdateTaskDetailsAsync(Guid taskId, string title, string description, string status, string priority, DateTime? startDate, DateTime? dueDate, Guid? assigneeId, string department, string riskLevel, int? storyPoints, string tags)
        {
            try
            {
                var response = await _supabase.From<PulseTask>()
                    .Where(t => t.Id == taskId)
                    .Set(t => t.Title, title)
                    .Set(t => t.Description, description)
                    .Set(t => t.Status, status)
                    .Set(t => t.Priority, priority)
                    .Set(t => t.StartDate, startDate)
                    .Set(t => t.DueDate, dueDate)
                    .Set(t => t.AssigneeId, assigneeId)
                    .Set(t => t.Department, department) // Novo
                    .Set(t => t.RiskLevel, riskLevel)   // Novo
                    .Set(t => t.StoryPoints, storyPoints) // Novo
                    .Set(t => t.Tags, tags)             // Novo
                    .Update();

                // Retorna a tarefa atualizada para usarmos no JS
                return response.Models.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao atualizar tarefa: {ex.Message}");
                return null;
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