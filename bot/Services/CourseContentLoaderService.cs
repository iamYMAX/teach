using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using OmnieyeBot.Models; // For ModuleContent

namespace OmnieyeBot.Services
{
    public class CourseContentLoaderService
    {
        private readonly string _baseDataPath;

        // Default constructor, assumes "Data" folder in the application's base directory
        public CourseContentLoaderService()
        {
            // Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) might be more robust
            // but for typical console app structure, AppContext.BaseDirectory is fine.
            // Ensure this path correctly resolves to the directory containing the "Data" folder.
            _baseDataPath = Path.Combine(AppContext.BaseDirectory, "Data", "Courses");
             // Create directory if it doesn't exist (e.g., first run or if manually deleted)
            if (!Directory.Exists(_baseDataPath))
            {
                Directory.CreateDirectory(_baseDataPath);
                Console.WriteLine($"[CourseContentLoaderService] Created directory: {_baseDataPath}");
            }
        }

        // Constructor allowing custom data path (useful for testing or different configurations)
        public CourseContentLoaderService(string customDataPath)
        {
            _baseDataPath = customDataPath; // Assumes customDataPath points directly to the "Courses" folder or equivalent
            if (!Directory.Exists(_baseDataPath))
            {
                try
                {
                    Directory.CreateDirectory(_baseDataPath);
                    Console.WriteLine($"[CourseContentLoaderService] Created custom directory: {_baseDataPath}");
                }
                catch (Exception ex)
                {
                     Console.WriteLine($"[CourseContentLoaderService] Error creating custom directory '{_baseDataPath}': {ex.Message}");
                     // Depending on requirements, might throw or fallback to a default. For now, just logs.
                }
            }
        }

        public async Task<ModuleContent?> LoadModuleFromFileAsync(string moduleId)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
            {
                Console.WriteLine("[CourseContentLoaderService] Error: Module ID cannot be null or empty.");
                return null;
            }

            // Sanitize moduleId to prevent path traversal issues, although less critical if only reading predefined files.
            // For now, assume moduleId is clean (e.g., "module1").
            string fileName = $"{moduleId}.json";
            string filePath = Path.Combine(_baseDataPath, fileName);

            Console.WriteLine($"[CourseContentLoaderService] Attempting to load module from: {filePath}");

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[CourseContentLoaderService] Error: File not found at {filePath}");
                return null;
            }

            try
            {
                string jsonContent = await File.ReadAllTextAsync(filePath);
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    Console.WriteLine($"[CourseContentLoaderService] Error: File at {filePath} is empty.");
                    return null;
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true // Good for flexibility if JSON naming varies slightly
                };

                ModuleContent? module = JsonSerializer.Deserialize<ModuleContent>(jsonContent, options);

                if (module == null)
                {
                    Console.WriteLine($"[CourseContentLoaderService] Error: Failed to deserialize JSON content from {filePath}. Result was null.");
                }
                else
                {
                    Console.WriteLine($"[CourseContentLoaderService] Successfully loaded and deserialized module '{module.Title}' (ID: {module.ModuleId}) from {filePath}");
                }
                return module;
            }
            catch (JsonException jsonEx)
            {
                Console.WriteLine($"[CourseContentLoaderService] JSON Deserialization Error for {filePath}: {jsonEx.Message}");
                if (jsonEx.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {jsonEx.InnerException.Message}");
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CourseContentLoaderService] General Error loading module from {filePath}: {ex.Message}");
                return null;
            }
        }

        // Potentially, a method to list available module IDs (e.g., by scanning file names)
        public Task<string[]> GetAvailableModuleIdsAsync()
        {
            if (!Directory.Exists(_baseDataPath))
            {
                 Console.WriteLine($"[CourseContentLoaderService] Data directory not found at {_baseDataPath} when trying to list modules.");
                return Task.FromResult(Array.Empty<string>());
            }

            var jsonFiles = Directory.GetFiles(_baseDataPath, "*.json");
            var moduleIds = jsonFiles
                .Select(Path.GetFileNameWithoutExtension)
                .Where(id => !string.IsNullOrWhiteSpace(id)) // Ensure ID is not null/empty after removing extension
                .ToArray();

            return Task.FromResult(moduleIds!); // Path.GetFileNameWithoutExtension can return null if file name is invalid
        }
    }
}
