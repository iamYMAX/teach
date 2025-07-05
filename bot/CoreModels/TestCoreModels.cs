using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Omnieye.Bot.States; // For TestDifficulty and LessonLevel enums

namespace Omnieye.Bot.CoreModels
{
    public class QuestionData
    {
        public string Text { get; set; } // Made settable for deserialization/editing
        public List<string> Options { get; set; } // Made settable
        public int CorrectOptionIndex { get; set; } // Made settable

        // Parameterless constructor for deserialization
        public QuestionData()
        {
            Text = string.Empty;
            Options = new List<string>();
        }

        [JsonConstructor] // Ensures this constructor is used for deserialization if multiple exist
        public QuestionData(string text, List<string> options, int correctOptionIndex)
        {
            Text = text;
            Options = options;
            CorrectOptionIndex = correctOptionIndex;
        }
    }

    public class TestData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString(); // Changed from int TestId to string Id
        public string TestName { get; set; } // Made settable
        public string Description { get; set; } = string.Empty;
        public List<QuestionData> Questions { get; set; } // Made settable
        public TestDifficulty Difficulty { get; set; } // Made settable
        public long CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Parameterless constructor for deserialization
        public TestData()
        {
            TestName = string.Empty;
            Questions = new List<QuestionData>();
        }

        // Original constructor adapted, primarily for internal use or testing if needed
        // For admin creation, properties will likely be set incrementally or via a parameterless constructor + property setters
        public TestData(string id, string testName, string description, List<QuestionData> questions, TestDifficulty difficulty, long createdBy)
        {
            Id = id;
            TestName = testName;
            Description = description;
            Questions = questions;
            Difficulty = difficulty;
            CreatedBy = createdBy;
            // CreatedAt is set by default
        }

        // Constructor for backward compatibility or specific scenarios if int ID is still used internally before switching to Guid strings
        // This constructor helps bridge the old int-based ID with the new string-based Guid ID.
        // It's important to ensure that 'activeTestsData' in Program.cs (if still used or during transition)
        // is updated to handle string IDs or that this constructor is used carefully.
        // For new tests created by admin, always use Guid.NewGuid().ToString() for Id.
         public TestData(int legacyTestId, string testName, List<QuestionData> questions, TestDifficulty difficulty = TestDifficulty.Easy)
        {
            Id = legacyTestId.ToString(); // Convert legacy int ID to string
            TestName = testName;
            Questions = questions;
            Difficulty = difficulty;
            Description = $"Imported legacy test. ID: {legacyTestId}"; // Default description
            CreatedBy = 0; // System or unknown creator for legacy data
            CreatedAt = DateTime.UtcNow; // Set creation time to now for imported data
        }
    }

    public class Flashcard
    {
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public LessonLevel Level { get; set; } = LessonLevel.Beginner; // Using LessonLevel for consistency

        public Flashcard(string question, string answer, LessonLevel level)
        {
            Question = question;
            Answer = answer;
            Level = level;
        }
        public Flashcard() { } // For deserialization or default instantiation
    }
}
