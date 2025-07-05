using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using OmnieyeBot.Models; // For CourseStructureRoot

namespace OmnieyeBot.Services
{
    public class CourseContentLoaderService
    {
        private readonly string _courseDataFilePath;
        private CourseStructureRoot? _cachedCourseStructure; // In-memory cache
        private DateTime _lastFileWriteTimeUtc; // For cache invalidation

        // Default constructor, assumes "Data/course_data.json"
        public CourseContentLoaderService(string courseFileName = "course_data.json")
        {
            // Base path is where the "Data" folder should reside relative to the application's execution directory
            string baseDataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");

            if (!Directory.Exists(baseDataDirectory))
            {
                try
                {
                    Directory.CreateDirectory(baseDataDirectory);
                    Console.WriteLine($"[CourseContentLoaderService] Created base data directory: {baseDataDirectory}");
                }
                catch (Exception ex)
                {
                     Console.WriteLine($"[CourseContentLoaderService] Error creating base data directory '{baseDataDirectory}': {ex.Message}");
                     // If this fails, loading will likely fail too.
                }
            }
            _courseDataFilePath = Path.Combine(baseDataDirectory, courseFileName);
        }

        public async Task<CourseStructureRoot?> GetOrLoadCourseStructureAsync(bool forceReload = false)
        {
            if (!File.Exists(_courseDataFilePath))
            {
                Console.WriteLine($"[CourseContentLoaderService] Error: Course data file not found at {_courseDataFilePath}");
                return null;
            }

            DateTime currentFileWriteTimeUtc = File.GetLastWriteTimeUtc(_courseDataFilePath);

            if (!forceReload && _cachedCourseStructure != null && _lastFileWriteTimeUtc == currentFileWriteTimeUtc)
            {
                Console.WriteLine($"[CourseContentLoaderService] Returning cached course structure. Last loaded: {_lastFileWriteTimeUtc}");
                return _cachedCourseStructure;
            }

            Console.WriteLine($"[CourseContentLoaderService] Attempting to load course structure from: {_courseDataFilePath}");

            try
            {
                string jsonContent = await File.ReadAllTextAsync(_courseDataFilePath);
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    Console.WriteLine($"[CourseContentLoaderService] Error: File at {_courseDataFilePath} is empty.");
                    _cachedCourseStructure = null; // Invalidate cache on error
                    return null;
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                CourseStructureRoot? courseStructure = JsonSerializer.Deserialize<CourseStructureRoot>(jsonContent, options);

                if (courseStructure == null)
                {
                    Console.WriteLine($"[CourseContentLoaderService] Error: Failed to deserialize JSON content from {_courseDataFilePath}. Result was null.");
                    _cachedCourseStructure = null; // Invalidate cache
                }
                else
                {
                    Console.WriteLine($"[CourseContentLoaderService] Successfully loaded and deserialized course structure: '{courseStructure.CourseTitle}' from {_courseDataFilePath}");
                    _cachedCourseStructure = courseStructure;
                    _lastFileWriteTimeUtc = currentFileWriteTimeUtc; // Update timestamp for cache
                }
                return _cachedCourseStructure;
            }
            catch (JsonException jsonEx)
            {
                Console.WriteLine($"[CourseContentLoaderService] JSON Deserialization Error for {_courseDataFilePath}: {jsonEx.Message}");
                if (jsonEx.InnerException != null) Console.WriteLine($"Inner Exception: {jsonEx.InnerException.Message}");
                _cachedCourseStructure = null; // Invalidate cache
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CourseContentLoaderService] General Error loading course structure from {_courseDataFilePath}: {ex.Message}");
                _cachedCourseStructure = null; // Invalidate cache
                return null;
            }
        }
    }
}
