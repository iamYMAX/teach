using Omnieye.Bot.CoreModels; // This will now bring in AdminLesson, AdminFlashcard, AdminTest, DifficultyLevel
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Omnieye.Bot.Admin
{
    public class AdminService
    {
        private const string DataDir = "Data";
        private const string LessonsFile = "lessons.json";
        private const string TestsFile = "tests.json";
        private const string FlashcardsFile = "flashcards.json";
        private const string LevelsFile = "levels.json";

        private readonly JsonSerializerOptions _jsonOptions;

        public AdminService()
        {
            if (!Directory.Exists(DataDir))
            {
                Directory.CreateDirectory(DataDir);
            }
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true, // For human-readable JSON files
                PropertyNameCaseInsensitive = true
            };

            // Initialize files if they don't exist or are empty
            InitializeJsonFile<List<AdminLesson>>(Path.Combine(DataDir, LessonsFile));
            InitializeJsonFile<List<AdminTest>>(Path.Combine(DataDir, TestsFile));
            InitializeJsonFile<List<AdminFlashcard>>(Path.Combine(DataDir, FlashcardsFile));
            InitializeJsonFile<List<DifficultyLevel>>(Path.Combine(DataDir, LevelsFile));
        }

        private async Task<List<T>> LoadDataAsync<T>(string fileName)
        {
            var filePath = Path.Combine(DataDir, fileName);
            if (!File.Exists(filePath))
            {
                // This case should ideally be handled by InitializeJsonFile,
                // but as a fallback, return an empty list.
                return new List<T>();
            }

            var json = await File.ReadAllTextAsync(filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<T>();
            }
            try
            {
                // Ensure that we are always returning a list, even if null is deserialized.
                return JsonSerializer.Deserialize<List<T>>(json, _jsonOptions) ?? new List<T>();
            }
            catch (JsonException ex)
            {
                // Log the exception details here if a logger is available
                Console.WriteLine($"Error deserializing {filePath}: {ex.Message}");
                // Decide on recovery strategy: return empty list, or re-throw, or try to fix file.
                // For now, returning an empty list to prevent crashes.
                return new List<T>();
            }
        }

        private async Task SaveDataAsync<T>(string fileName, List<T> data)
        {
            var filePath = Path.Combine(DataDir, fileName);
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }

        private void InitializeJsonFile<TCollection>(string filePath) where TCollection : new()
        {
            if (!File.Exists(filePath))
            {
                 // If file doesn't exist, create it with an empty collection.
                var emptyCollection = new TCollection();
                var jsonData = JsonSerializer.Serialize(emptyCollection, _jsonOptions);
                File.WriteAllText(filePath, jsonData);
            }
            else
            {
                // If file exists, check if it's empty or just whitespace.
                var content = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(content))
                {
                    // File is empty or whitespace, overwrite with an empty collection.
                    var emptyCollection = new TCollection();
                    var jsonData = JsonSerializer.Serialize(emptyCollection, _jsonOptions);
                    File.WriteAllText(filePath, jsonData);
                }
                // If file exists and has content, assume it's valid or will be handled by LoadDataAsync.
            }
        }

        // Lesson Methods
        public async Task<List<AdminLesson>> GetLessonsAsync() => await LoadDataAsync<AdminLesson>(LessonsFile);
        public async Task SaveLessonsAsync(List<AdminLesson> lessons) => await SaveDataAsync(LessonsFile, lessons);

        public async Task AddLessonAsync(AdminLesson newLesson)
        {
            var lessons = await GetLessonsAsync();
            // Optional: Check for duplicate lesson names, if desired
            // if (lessons.Any(l => l.Name.Equals(newLesson.Name, StringComparison.OrdinalIgnoreCase)))
            // {
            //     throw new InvalidOperationException($"Lesson with name '{newLesson.Name}' already exists.");
            // }
            lessons.Add(newLesson);
            await SaveLessonsAsync(lessons);
        }

        public async Task<AdminLesson?> GetLessonByIdAsync(string lessonId)
        {
            var lessons = await GetLessonsAsync();
            return lessons.FirstOrDefault(l => l.Id == lessonId);
        }

        public async Task<bool> UpdateLessonAsync(AdminLesson updatedLesson)
        {
            var lessons = await GetLessonsAsync();
            var lessonIndex = lessons.FindIndex(l => l.Id == updatedLesson.Id);
            if (lessonIndex == -1) return false;

            // Optional: Check for duplicate name if name is being changed and needs to be unique
            // if (lessons.Any(l => l.Name.Equals(updatedLesson.Name, StringComparison.OrdinalIgnoreCase) && l.Id != updatedLesson.Id))
            // {
            //      throw new InvalidOperationException($"Another lesson with name '{updatedLesson.Name}' already exists.");
            // }

            lessons[lessonIndex] = updatedLesson;
            await SaveLessonsAsync(lessons);
            return true;
        }

        public async Task<bool> DeleteLessonAsync(string lessonId)
        {
            var lessons = await GetLessonsAsync();
            var lessonToDelete = lessons.FirstOrDefault(l => l.Id == lessonId);
            if (lessonToDelete == null) return false;

            lessons.Remove(lessonToDelete);
            await SaveLessonsAsync(lessons);
            return true;
        }

        // Test Methods
        public async Task<List<AdminTest>> GetTestsAsync() => await LoadDataAsync<AdminTest>(TestsFile);
        public async Task SaveTestsAsync(List<AdminTest> tests) => await SaveDataAsync(TestsFile, tests);

        public async Task AddTestAsync(AdminTest newTest) // newTest will have Name and LevelId set, Questions will be empty
        {
            var tests = await GetTestsAsync();
            // Optional: Check for duplicate test names
            if (tests.Any(t => t.TestName.Equals(newTest.TestName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Тест с названием '{newTest.TestName}' уже существует.");
            }
            tests.Add(newTest);
            await SaveTestsAsync(tests);
        }

        public async Task<AdminTest?> GetTestByIdAsync(string testId)
        {
            var tests = await GetTestsAsync();
            return tests.FirstOrDefault(t => t.Id == testId);
        }

        // Simplified: No update method for test details in this step. Can be added later.
        // public async Task<bool> UpdateTestDetailsAsync(string testId, string newName, string newLevelId) { ... }

        public async Task<bool> DeleteTestAsync(string testId)
        {
            var tests = await GetTestsAsync();
            var testToDelete = tests.FirstOrDefault(t => t.Id == testId);
            if (testToDelete == null) return false;

            tests.Remove(testToDelete);
            await SaveTestsAsync(tests);
            return true;
        }

        // Flashcard Methods
        public async Task<List<AdminFlashcard>> GetFlashcardsAsync() => await LoadDataAsync<AdminFlashcard>(FlashcardsFile);
        public async Task SaveFlashcardsAsync(List<AdminFlashcard> flashcards) => await SaveDataAsync(FlashcardsFile, flashcards);

        public async Task AddFlashcardAsync(AdminFlashcard newFlashcard)
        {
            var flashcards = await GetFlashcardsAsync();
            // Optional: Check for duplicate flashcard questions if desired, though less common.
            // if (flashcards.Any(fc => fc.Question.Equals(newFlashcard.Question, StringComparison.OrdinalIgnoreCase)))
            // {
            //     throw new InvalidOperationException($"Flashcard with question '{newFlashcard.Question}' already exists.");
            // }
            flashcards.Add(newFlashcard);
            await SaveFlashcardsAsync(flashcards);
        }

        public async Task<AdminFlashcard?> GetFlashcardByIdAsync(string flashcardId)
        {
            var flashcards = await GetFlashcardsAsync();
            return flashcards.FirstOrDefault(fc => fc.Id == flashcardId);
        }

        public async Task<bool> UpdateFlashcardAsync(AdminFlashcard updatedFlashcard)
        {
            var flashcards = await GetFlashcardsAsync();
            var flashcardIndex = flashcards.FindIndex(fc => fc.Id == updatedFlashcard.Id);
            if (flashcardIndex == -1) return false;

            flashcards[flashcardIndex] = updatedFlashcard;
            await SaveFlashcardsAsync(flashcards);
            return true;
        }

        public async Task<bool> DeleteFlashcardAsync(string flashcardId)
        {
            var flashcards = await GetFlashcardsAsync();
            var flashcardToDelete = flashcards.FirstOrDefault(fc => fc.Id == flashcardId);
            if (flashcardToDelete == null) return false;

            flashcards.Remove(flashcardToDelete);
            await SaveFlashcardsAsync(flashcards);
            return true;
        }

        // DifficultyLevel Methods
        public async Task<List<DifficultyLevel>> GetDifficultyLevelsAsync() => await LoadDataAsync<DifficultyLevel>(LevelsFile);

        public async Task SaveDifficultyLevelsAsync(List<DifficultyLevel> levels) => await SaveDataAsync(LevelsFile, levels);

        public async Task AddDifficultyLevelAsync(DifficultyLevel newLevel)
        {
            var levels = await GetDifficultyLevelsAsync();
            // Check for duplicates by name, case-insensitive
            if (levels.Any(l => l.Name.Equals(newLevel.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Difficulty level with name '{newLevel.Name}' already exists.");
            }
            levels.Add(newLevel);
            await SaveDifficultyLevelsAsync(levels);
        }

        public async Task<DifficultyLevel?> GetDifficultyLevelByIdAsync(string levelId)
        {
            var levels = await GetDifficultyLevelsAsync();
            return levels.FirstOrDefault(l => l.Id == levelId);
        }

        public async Task<bool> RenameDifficultyLevelAsync(string levelId, string newName)
        {
            var levels = await GetDifficultyLevelsAsync();
            var levelToRename = levels.FirstOrDefault(l => l.Id == levelId);
            if (levelToRename == null) return false;

            // Check if new name already exists (and it's not the same level)
            if (levels.Any(l => l.Name.Equals(newName, StringComparison.OrdinalIgnoreCase) && l.Id != levelId))
            {
                 throw new InvalidOperationException($"Another difficulty level with name '{newName}' already exists.");
            }

            levelToRename.Name = newName;
            await SaveDifficultyLevelsAsync(levels);
            return true;
        }

        public async Task<bool> DeleteDifficultyLevelAsync(string levelId)
        {
            var levels = await GetDifficultyLevelsAsync();
            var levelToDelete = levels.FirstOrDefault(l => l.Id == levelId);
            if (levelToDelete == null) return false;

            // TODO: Add dependency check here in the future (e.g., check if any lessons/tests use this level).
            // For now, directly remove.
            levels.Remove(levelToDelete);
            await SaveDifficultyLevelsAsync(levels);
            return true;
        }
    }
}
