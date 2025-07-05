using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Omnieye.Bot.CoreModels;

namespace Omnieye.Bot.Services
{
    public class AdminDataService
    {
        private readonly string _lessonsFilePath;
        private readonly string _testsFilePath;
        private readonly JsonSerializerOptions _jsonOptions;

        private List<Lesson> _lessonsCache;
        private List<TestData> _testsCache;

        public AdminDataService(string dataDirectory = "bot_data")
        {
            // Ensure the data directory exists
            if (!Directory.Exists(dataDirectory))
            {
                Directory.CreateDirectory(dataDirectory);
            }

            _lessonsFilePath = Path.Combine(dataDirectory, "lessons.json");
            _testsFilePath = Path.Combine(dataDirectory, "tests.json");
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                // Converters can be added here if needed for complex types or enums stored as strings
            };

            _lessonsCache = LoadData<Lesson>(_lessonsFilePath).Result ?? new List<Lesson>();
            _testsCache = LoadData<TestData>(_testsFilePath).Result ?? new List<TestData>();
        }

        private async Task<List<T>> LoadData<T>(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Data file not found: {filePath}. Returning empty list.");
                return new List<T>();
            }

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var data = JsonSerializer.Deserialize<List<T>>(json, _jsonOptions);
                Console.WriteLine($"Successfully loaded {data?.Count ?? 0} items from {filePath}.");
                return data ?? new List<T>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading data from {filePath}: {ex.Message}. Returning empty list.");
                return new List<T>();
            }
        }

        private async Task SaveData<T>(string filePath, List<T> data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, _jsonOptions);
                await File.WriteAllTextAsync(filePath, json);
                Console.WriteLine($"Successfully saved {data.Count} items to {filePath}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving data to {filePath}: {ex.Message}");
            }
        }

        // Lesson Management
        public List<Lesson> GetAllLessons() => _lessonsCache.ToList(); // Return a copy

        public Lesson? GetLessonById(string id) => _lessonsCache.FirstOrDefault(l => l.Id == id);

        public async Task AddLesson(Lesson lesson)
        {
            if (lesson == null) throw new ArgumentNullException(nameof(lesson));
            if (string.IsNullOrWhiteSpace(lesson.Id)) lesson.Id = Guid.NewGuid().ToString();

            _lessonsCache.Add(lesson);
            await SaveData(_lessonsFilePath, _lessonsCache);
        }

        public async Task UpdateLesson(Lesson lesson)
        {
            if (lesson == null) throw new ArgumentNullException(nameof(lesson));
            var existingLesson = _lessonsCache.FirstOrDefault(l => l.Id == lesson.Id);
            if (existingLesson != null)
            {
                // Update properties. A more sophisticated approach might use reflection or AutoMapper.
                existingLesson.Title = lesson.Title;
                existingLesson.Description = lesson.Description;
                existingLesson.Content = lesson.Content;
                existingLesson.Level = lesson.Level;
                // CreatedBy and CreatedAt should generally not be updated after creation.
                await SaveData(_lessonsFilePath, _lessonsCache);
            }
            else
            {
                throw new KeyNotFoundException($"Lesson with ID '{lesson.Id}' not found.");
            }
        }

        public async Task DeleteLesson(string id)
        {
            var lessonToRemove = _lessonsCache.FirstOrDefault(l => l.Id == id);
            if (lessonToRemove != null)
            {
                _lessonsCache.Remove(lessonToRemove);
                await SaveData(_lessonsFilePath, _lessonsCache);
            }
            else
            {
                throw new KeyNotFoundException($"Lesson with ID '{id}' not found for deletion.");
            }
        }

        // Test Management
        public List<TestData> GetAllTests() => _testsCache.ToList(); // Return a copy

        public TestData? GetTestById(string id) => _testsCache.FirstOrDefault(t => t.Id == id);

        public async Task AddTest(TestData test)
        {
            if (test == null) throw new ArgumentNullException(nameof(test));
            if (string.IsNullOrWhiteSpace(test.Id)) test.Id = Guid.NewGuid().ToString();

            _testsCache.Add(test);
            await SaveData(_testsFilePath, _testsCache);
        }

        public async Task UpdateTest(TestData test)
        {
            if (test == null) throw new ArgumentNullException(nameof(test));
            var existingTest = _testsCache.FirstOrDefault(t => t.Id == test.Id);
            if (existingTest != null)
            {
                existingTest.TestName = test.TestName;
                existingTest.Description = test.Description;
                existingTest.Questions = test.Questions; // Assuming wholesale replacement of questions list
                existingTest.Difficulty = test.Difficulty;
                // CreatedBy and CreatedAt should generally not be updated.
                await SaveData(_testsFilePath, _testsCache);
            }
            else
            {
                throw new KeyNotFoundException($"Test with ID '{test.Id}' not found.");
            }
        }

        public async Task DeleteTest(string id)
        {
            var testToRemove = _testsCache.FirstOrDefault(t => t.Id == id);
            if (testToRemove != null)
            {
                _testsCache.Remove(testToRemove);
                await SaveData(_testsFilePath, _testsCache);
            }
            else
            {
                throw new KeyNotFoundException($"Test with ID '{id}' not found for deletion.");
            }
        }
    }
}
