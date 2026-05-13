using System;
using System.ComponentModel.DataAnnotations;

namespace PulseBoardMigration.Models
{
    public class PulseTask
    {
        public Guid Id { get; set; }

        // Chaves Estrangeiras para saber onde esta tarefa está
        public Guid BoardId { get; set; }
        public Guid ColumnId { get; set; }

        [Required(ErrorMessage = "O título da tarefa é obrigatório.")]
        public string Title { get; set; }

        public string Description { get; set; }

        public string Priority { get; set; } // Ex: Baixa, Média, Alta

        public string Status { get; set; } // Ex: A Fazer, Em Progresso, Concluído

        public DateTime? DueDate { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
