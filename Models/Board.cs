using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Postgrest.Attributes;
using Postgrest.Models;
using Newtonsoft.Json; // Usado para mapear o JSON interno do Supabase

namespace PulseBoardMigration.Models
{
    // Classe auxiliar para ler o JSON de dentro da coluna "settings"
    public class BoardColumnSetting
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }
    }

    [Table("boards")]
    public class Board : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; }

        // MUDOU DE Title PARA Name
        [Column("name")]
        [Required(ErrorMessage = "O nome é obrigatório")]
        public string Name { get; set; }

        [Column("description")]
        public string Description { get; set; }

        [Column("owner_id")]
        public Guid OwnerId { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; }

        // Coluna nova identificada no JSON
        [Column("status")]
        public string Status { get; set; }

        // Mapeamento do JSONB das colunas do Kanban
        [Column("settings")]
        public List<BoardColumnSetting> Settings { get; set; }
    }
}