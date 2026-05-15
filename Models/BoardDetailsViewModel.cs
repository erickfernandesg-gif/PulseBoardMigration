using System.Collections.Generic;

namespace PulseBoardMigration.Models
{
    // Esta classe serve apenas como "carteiro" para levar os dados para a tela
    public class BoardDetailsViewModel
    {
        public Board Board { get; set; }
        public List<PulseTask> Tasks { get; set; }
        public List<Column> Columns { get; set; }
    }
}