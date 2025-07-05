using System;
using Omnieye.Bot.States; // For LessonLevel enum

namespace Omnieye.Bot.CoreModels
{
    public class Lesson
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty; // Renamed Summary to Description for clarity
        public string Content { get; set; } = string.Empty;
        public LessonLevel Level { get; set; } = LessonLevel.Beginner;
        public long CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Constructor for easy initialization
        public Lesson(string title, string description, string content, LessonLevel level, long createdBy)
        {
            Title = title;
            Description = description;
            Content = content;
            Level = level;
            CreatedBy = createdBy;
            // Id and CreatedAt are set by default
        }

        // Parameterless constructor for deserialization and other uses
        public Lesson() { }
    }
}
