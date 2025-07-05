// Path: bot/CoreModels/BotData.cs
using System.Collections.Generic;
using Omnieye.Bot.States;         // For UserProfile
using Omnieye.Bot.Models;         // For the original Test model (Omnieye.Bot.Models.Test)

// Types from current namespace Omnieye.Bot.CoreModels are directly accessible:
// Lesson (original from LessonModels.cs), Flashcard (original from TestCoreModels.cs)
// AdminLesson, AdminFlashcard, AdminTest, DifficultyLevel
// BotData itself

namespace Omnieye.Bot.CoreModels
{
    public class BotData
    {
        public List<UserProfile> Users { get; set; } = new();

        // Properties for original bot data structures (as potentially loaded by Program.cs at startup)
        public List<Lesson> ExistingLessons { get; set; } = new();       // Refers to CoreModels.Lesson (original definition)
        public List<Flashcard> ExistingFlashcards { get; set; } = new(); // Refers to CoreModels.Flashcard (original definition from TestCoreModels.cs)
        public List<Omnieye.Bot.Models.Test> ExistingTests { get; set; } = new(); // Explicitly using original Test from Models namespace

        // Properties for Admin Panel Data (managed via JSON files in Data/ a_nd AdminService)
        public List<AdminLesson> AdminPanelLessons { get; set; } = new();
        public List<AdminFlashcard> AdminPanelFlashcards { get; set; } = new();
        public List<AdminTest> AdminPanelTests { get; set; } = new();
        public List<DifficultyLevel> AdminPanelDifficultyLevels { get; set; } = new();
    }
}
