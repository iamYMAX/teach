using System;
using System.IO;

namespace Omnieye.Bot
{
    public class MaterialLoader
    {
        private readonly string _basePath;

        public MaterialLoader(string basePath = "materials/junior_admin")
        {
            // Ensure the base path is correctly pointing to the materials directory
            // This might need adjustment depending on where the executable runs from
            _basePath = Path.GetFullPath(basePath);
            if (!Directory.Exists(_basePath))
            {
                Console.WriteLine($"Warning: Material directory not found at {_basePath}. Ensure materials are correctly placed.");
                // Or throw new DirectoryNotFoundException($"Material directory not found: {_basePath}");
            }
        }

        public string? LoadTheory(int chapterNumber)
        {
            if (chapterNumber <= 0) return null;
            string fileName = $"theory_chapter{chapterNumber}.md";
            return LoadMaterialFile(fileName);
        }

        public string? LoadPractice(int assignmentNumber)
        {
            if (assignmentNumber <= 0) return null;
            string fileName = $"practice_assignment{assignmentNumber}.md";
            return LoadMaterialFile(fileName);
        }

        private string? LoadMaterialFile(string fileName)
        {
            try
            {
                string filePath = Path.Combine(_basePath, fileName);
                if (File.Exists(filePath))
                {
                    return File.ReadAllText(filePath);
                }
                else
                {
                    Console.WriteLine($"Material file not found: {filePath}");
                    return null;
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Error reading material file {fileName}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred while loading {fileName}: {ex.Message}");
                return null;
            }
        }
    }
}
