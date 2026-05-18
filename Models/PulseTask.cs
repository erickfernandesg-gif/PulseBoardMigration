using System;
using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;

namespace PulseBoardMigration.Models
{
    [Table("tasks")] // Certifique-se de que o nome da tabela no Supabase é realmente 'tasks' (no original era 'pulse_tasks')
    public class PulseTask : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        [Column("board_id")]
        public Guid BoardId { get; set; }

        [Column("title")]
        [Required(ErrorMessage = "O título é obrigatório")]
        public string Title { get; set; }

        [Column("description")]
        public string Description { get; set; }

        // É AQUI que o C# liga a tarefa à coluna do JSON (ex: "todo", "done")
        [Column("status")]
        public string Status { get; set; }

        [Column("priority")]
        public string Priority { get; set; }

        // A posição é vital para fazermos o Drag and Drop funcionar depois!
        //[Column("position")]
        //public int Position { get; set; }

        // ==========================================
        // CAMPOS PARA GANTT E RESPONSÁVEL
        // ==========================================

        [Column("start_date")]
        public DateTime? StartDate { get; set; }

        [Column("assignee_id")]
        public Guid? AssigneeId { get; set; }

        // ==========================================
        // NOVOS CAMPOS AVANÇADOS (IGUAIS AO NEXT.JS)
        // ==========================================

        [Column("department")]
        public string Department { get; set; }

        [Column("risk_level")]
        public string RiskLevel { get; set; }

        [Column("story_points")]
        public int? StoryPoints { get; set; }

        // Como recebemos do input HTML uma string separada por vírgulas (ex: "bug, frontend"), 
        // vamos armazenar como string simples para facilitar a migração.
        [Column("tags")]
        public string Tags { get; set; }

        // ==========================================

        [Column("due_date")]
        public DateTime? DueDate { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        // Dica de Sênior: Deixei comentado para evitar o mesmo erro do 'boards'. 
        // Se a sua tabela 'tasks' lá no Supabase também NÃO tiver o 'updated_at', mantenha comentado.
        // Se tiver, pode remover as "//".
        // [Column("updated_at")]
        // public DateTime UpdatedAt { get; set; }
    }
}