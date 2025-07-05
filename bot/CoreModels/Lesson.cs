namespace Omnieye.Bot.CoreModels
{
    public class Lesson
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DifficultyLevelId { get; set; } = string.Empty;
        // Consider adding public string DifficultyLevelName { get; set; } for easier display
    }
}
