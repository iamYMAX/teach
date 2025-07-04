using System.Collections.Generic;
using Newtonsoft.Json; // Make sure Newtonsoft.Json is referenced in the project

namespace Omnieye.Bot.Models
{
    public class TestQuestion
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("questionText")]
        public string QuestionText { get; set; } = string.Empty;

        [JsonProperty("options")]
        public List<string> Options { get; set; } = new List<string>();

        [JsonProperty("correctAnswer")]
        public string CorrectAnswer { get; set; } = string.Empty;

        // Helper to get the 0-based index of the correct answer
        [JsonIgnore]
        public int CorrectAnswerIndex => Options.IndexOf(CorrectAnswer);
    }

    public class Test
    {
        [JsonProperty("courseName")]
        public string CourseName { get; set; } = string.Empty;

        [JsonProperty("questions")]
        public List<TestQuestion> Questions { get; set; } = new List<TestQuestion>();
    }

    public class UserTestState
    {
        public Test CurrentTest { get; }
        public int CurrentQuestionIndex { get; set; }
        public List<int> UserAnswers { get; } // Stores index of selected option for each question answered
        public bool IsTestActive => CurrentTest != null && CurrentQuestionIndex < CurrentTest.Questions.Count;

        public UserTestState(Test test)
        {
            CurrentTest = test;
            CurrentQuestionIndex = 0;
            UserAnswers = new List<int>();
        }

        public TestQuestion GetCurrentQuestion()
        {
            if (IsTestActive)
            {
                return CurrentTest.Questions[CurrentQuestionIndex];
            }
            return null!; // Or throw an exception
        }

        public void SubmitAnswer(int answerIndex)
        {
            if (IsTestActive)
            {
                UserAnswers.Add(answerIndex);
                CurrentQuestionIndex++;
            }
        }

        public (int Score, int TotalQuestions) CalculateScore()
        {
            int score = 0;
            for (int i = 0; i < UserAnswers.Count; i++)
            {
                if (i < CurrentTest.Questions.Count && UserAnswers[i] == CurrentTest.Questions[i].CorrectAnswerIndex)
                {
                    score++;
                }
            }
            return (score, CurrentTest.Questions.Count);
        }
    }
}
