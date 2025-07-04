using System;
using System.IO;
using Newtonsoft.Json;
using Omnieye.Bot.Models; // Assuming TestModels.cs is in a Models sub-namespace or accessible

namespace Omnieye.Bot.Services
{
    public class TestLoaderService
    {
        private readonly string _testsFilePath;

        // In the future, lessonOrTestId might be used to select different JSON files or sections
        public TestLoaderService(string basePath = "materials/junior_admin", string testFileName = "tests_junior_admin.json")
        {
            _testsFilePath = Path.Combine(Path.GetFullPath(basePath), testFileName);
        }

        public Test? LoadTest()
        {
            try
            {
                if (!File.Exists(_testsFilePath))
                {
                    Console.WriteLine($"Error: Test file not found at {_testsFilePath}");
                    return null;
                }

                string jsonData = File.ReadAllText(_testsFilePath);
                Test? testData = JsonConvert.DeserializeObject<Test>(jsonData);

                if (testData == null)
                {
                    Console.WriteLine($"Error: Could not deserialize test data from {_testsFilePath}. Check JSON structure.");
                    return null;
                }

                // Basic validation
                if (testData.Questions == null || testData.Questions.Count == 0)
                {
                    Console.WriteLine($"Warning: Test data loaded from {_testsFilePath} but contains no questions.");
                }

                return testData;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error deserializing test JSON from {_testsFilePath}: {ex.Message}");
                return null;
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Error reading test file {_testsFilePath}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred while loading test from {_testsFilePath}: {ex.Message}");
                return null;
            }
        }
    }
}
