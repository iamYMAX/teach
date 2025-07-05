// Path: bot/CoreModels/AdminFlashcard.cs
namespace Omnieye.Bot.CoreModels
{
    public class AdminFlashcard // Renamed from Flashcard
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString();
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public string DifficultyLevelId { get; set; } = string.Empty;
    }
}
