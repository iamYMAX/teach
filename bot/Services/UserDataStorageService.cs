using System;
using System.IO;
using System.Text.Json;
using Omnieye.Bot.States; // For UserProfile

namespace Omnieye.Bot.Services
{
    public class UserDataStorageService
    {
        private readonly string _storageDirectory;

        public UserDataStorageService(string storageFolderName = "user_data")
        {
            // Base directory where the application is running
            string baseDirectory = AppContext.BaseDirectory;
            _storageDirectory = Path.Combine(baseDirectory, storageFolderName);

            if (!Directory.Exists(_storageDirectory))
            {
                Directory.CreateDirectory(_storageDirectory);
                Console.WriteLine($"Created user data storage directory at: {_storageDirectory}");
            }
            else
            {
                Console.WriteLine($"User data storage directory found at: {_storageDirectory}");
            }
        }

        private string GetProfilePath(long userId)
        {
            return Path.Combine(_storageDirectory, $"{userId}_profile.json");
        }

        public void SaveProfile(UserProfile profile)
        {
            if (profile == null)
            {
                Console.WriteLine($"Warning: Attempted to save a null profile for UserId {profile?.UserId}. Operation skipped.");
                return;
            }

            try
            {
                string filePath = GetProfilePath(profile.UserId);
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(profile, options);
                File.WriteAllText(filePath, json);
                Console.WriteLine($"Profile saved for UserId {profile.UserId} to {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving profile for UserId {profile.UserId}: {ex.Message}");
                // Depending on requirements, might re-throw or handle more gracefully
            }
        }

        public UserProfile LoadProfile(long userId)
        {
            string filePath = GetProfilePath(userId);
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"No profile file found for UserId {userId}. Creating new profile.");
                var newProfile = new UserProfile(userId); // UserId is set, RegisteredAt defaults to UtcNow
                // SaveProfile(newProfile); // Optionally save the new profile immediately
                return newProfile;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                UserProfile? profile = JsonSerializer.Deserialize<UserProfile>(json);
                if (profile != null)
                {
                    Console.WriteLine($"Profile loaded for UserId {userId} from {filePath}");
                    return profile;
                }
                else
                {
                    Console.WriteLine($"Warning: Failed to deserialize profile for UserId {userId} from {filePath}. Returning new profile.");
                    return new UserProfile(userId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading profile for UserId {userId}: {ex.Message}. Returning new profile.");
                return new UserProfile(userId); // Return a new profile in case of error
            }
        }

        public List<UserProfile> LoadAllProfiles()
        {
            var profiles = new List<UserProfile>();
            if (!Directory.Exists(_storageDirectory))
            {
                Console.WriteLine($"Storage directory not found at {_storageDirectory} when trying to load all profiles.");
                return profiles; // Return empty list
            }

            var profileFiles = Directory.GetFiles(_storageDirectory, "*_profile.json");
            Console.WriteLine($"Found {profileFiles.Length} profile files in {_storageDirectory}.");

            foreach (var filePath in profileFiles)
            {
                try
                {
                    // Extract UserId from filename, e.g., "12345_profile.json" -> 12345
                    string fileName = Path.GetFileNameWithoutExtension(filePath); // "12345_profile"
                    string userIdString = fileName.Substring(0, fileName.IndexOf("_profile"));
                    if (long.TryParse(userIdString, out long userId))
                    {
                        string json = File.ReadAllText(filePath);
                        UserProfile? profile = JsonSerializer.Deserialize<UserProfile>(json);
                        if (profile != null)
                        {
                            // Ensure UserId is consistent if it was part of the file, or set it from filename
                            if (profile.UserId == 0) profile.UserId = userId;
                            else if (profile.UserId != userId) {
                                Console.WriteLine($"Warning: Mismatch UserId in filename ({userId}) and file content ({profile.UserId}) for {filePath}. Using UserId from filename.");
                                profile.UserId = userId; // Prioritize filename for consistency if there's a mismatch
                            }
                            profiles.Add(profile);
                        }
                        else
                        {
                            Console.WriteLine($"Warning: Failed to deserialize profile from {filePath}. Skipping.");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Warning: Could not parse UserId from filename {fileName}. Skipping file {filePath}.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading profile from file {filePath}: {ex.Message}. Skipping.");
                    // Continue to next file
                }
            }
            Console.WriteLine($"Successfully loaded {profiles.Count} profiles out of {profileFiles.Length} files found.");
            return profiles;
        }
    }
}
