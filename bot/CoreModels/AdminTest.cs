// Path: bot/CoreModels/AdminTest.cs
using System.Collections.Generic;

namespace Omnieye.Bot.CoreModels
{
    public class AdminQuestion // Renamed from Question
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString();
        public string QuestionText { get; set; } = string.Empty;
        public List<string> AnswerOptions { get; set; } = new List<string>();
        public int CorrectAnswerIndex { get; set; }
    }

    public class AdminTest // Renamed from Test
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString();
        public string TestName { get; set; } = string.Empty;
        public string DifficultyLevelId { get; set; } = string.Empty;
        public List<AdminQuestion> Questions { get; set; } = new List<AdminQuestion>(); // Uses AdminQuestion
    }
}
