// Path: bot/CoreModels/AdminLesson.cs
namespace Omnieye.Bot.CoreModels
{
    public class AdminLesson // Renamed from Lesson
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DifficultyLevelId { get; set; } = string.Empty;
    }
}
