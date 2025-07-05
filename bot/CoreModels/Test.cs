using System.Collections.Generic;

namespace Omnieye.Bot.CoreModels
{
    public class Question
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString();
        public string QuestionText { get; set; } = string.Empty;
        public List<string> AnswerOptions { get; set; } = new List<string>();
        public int CorrectAnswerIndex { get; set; } // Or string CorrectAnswerValue
        // public string Explanation { get; set; } // Optional: explanation for the correct answer
    }

    public class Test
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString();
        public string TestName { get; set; } = string.Empty;
        public string DifficultyLevelId { get; set; } = string.Empty;
        public List<Question> Questions { get; set; } = new List<Question>();
        // Consider adding public string DifficultyLevelName { get; set; }
    }
}
