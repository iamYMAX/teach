using System.Collections.Generic;
using Omnieye.Bot.States; // For TestDifficulty enum

namespace Omnieye.Bot.CoreModels
{
    public class QuestionData
    {
        public string Text { get; }
        public List<string> Options { get; }
        public int CorrectOptionIndex { get; }

        public QuestionData(string text, List<string> options, int correctOptionIndex)
        {
            Text = text;
            Options = options;
            CorrectOptionIndex = correctOptionIndex;
        }
    }

    public class TestData
    {
        public int TestId { get; }
        public string TestName { get; }
        public List<QuestionData> Questions { get; }
        public TestDifficulty Difficulty { get; }

        public TestData(int testId, string testName, List<QuestionData> questions, TestDifficulty difficulty = TestDifficulty.Easy)
        {
            TestId = testId;
            TestName = testName;
            Questions = questions;
            Difficulty = difficulty;
        }
    }
}
