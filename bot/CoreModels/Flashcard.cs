namespace Omnieye.Bot.CoreModels
{
    public class Flashcard
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString();
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public string DifficultyLevelId { get; set; } = string.Empty;
        // Consider adding public string DifficultyLevelName { get; set; }
    }
}
