using Omnieye.Bot.States; // For LessonLevel enum

namespace Omnieye.Bot.CoreModels
{
    public class Lesson
    {
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public LessonLevel Level { get; set; } = LessonLevel.Beginner;

        // Constructor for easy initialization
        public Lesson(string title, string summary, string content, LessonLevel level)
        {
            Title = title;
            Summary = summary;
            Content = content;
            Level = level;
        }

        // Parameterless constructor for potential deserialization or other uses
        public Lesson() { }
    }
}
