using System;
using System.Collections.Concurrent;
using System.Collections.Generic; // For List<UserProfile>
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Omnieye.Bot.States; // For UserSession, TestHistoryEntry, UserProfile
using Omnieye.Bot.CoreModels; // For TestData

namespace Omnieye.Bot.Services
{
    public class UserSessionService
    {
        private readonly string _persistenceFilePath;
        private ConcurrentDictionary<long, UserSession> _userSessions;
        private readonly UserDataStorageService _userDataStorageService;

        private const string DefaultPersistenceFileName = "user_sessions.json";

        public UserSessionService(string persistenceFolderPath = "")
        {
            _userDataStorageService = new UserDataStorageService();

            if (string.IsNullOrWhiteSpace(persistenceFolderPath))
            {
                persistenceFolderPath = Path.Combine(AppContext.BaseDirectory, "data");
                if (!Directory.Exists(persistenceFolderPath)) // Ensure directory exists
                {
                    Directory.CreateDirectory(persistenceFolderPath);
                }
            }
            _persistenceFilePath = Path.Combine(persistenceFolderPath, DefaultPersistenceFileName);
            _userSessions = LoadSessionsFromFile();
        }

        private ConcurrentDictionary<long, UserSession> LoadSessionsFromFile()
        {
            try
            {
                if (File.Exists(_persistenceFilePath))
                {
                    string jsonData = File.ReadAllText(_persistenceFilePath);
                    var sessions = JsonConvert.DeserializeObject<ConcurrentDictionary<long, UserSession>>(jsonData);
                    if (sessions != null)
                    {
                        Console.WriteLine($"Successfully loaded {sessions.Count} user sessions from {_persistenceFilePath}");
                        foreach (var kvp in sessions)
                        {
                            var session = kvp.Value;
                            long userId = kvp.Key;
                            if (session.UserId == 0 && userId != 0) session.UserId = userId;

                            if (session.Profile == null)
                            {
                                session.Profile = _userDataStorageService.LoadProfile(userId);
                            }
                            else if (session.Profile.UserId == 0 && userId != 0)
                            {
                                session.Profile.UserId = userId;
                            }
                            if (session.TestHistory == null)
                            {
                                session.TestHistory = new List<TestHistoryEntry>();
                            }
                            if (session.LastShownLessonTitles == null)
                            {
                                session.LastShownLessonTitles = new List<string>();
                            }
                            if (session.LastShownTestList == null)
                            {
                                session.LastShownTestList = new List<TestData>();
                            }
                        }
                        return sessions;
                    }
                    Console.WriteLine($"Warning: Could not deserialize user sessions from {_persistenceFilePath}. Starting with empty sessions.");
                }
                else
                {
                    Console.WriteLine($"User sessions file not found at {_persistenceFilePath}. Starting with empty sessions.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading user sessions from {_persistenceFilePath}: {ex.Message}. Starting with empty sessions.");
            }
            return new ConcurrentDictionary<long, UserSession>();
        }

        public async Task SaveSessionsToFileAsync()
        {
            try
            {
                string jsonData = JsonConvert.SerializeObject(_userSessions, Formatting.Indented);
                await File.WriteAllTextAsync(_persistenceFilePath, jsonData);
                Console.WriteLine($"Successfully saved {_userSessions.Count} user sessions to {_persistenceFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving user sessions to {_persistenceFilePath}: {ex.Message}");
            }
        }

        public UserSession GetUserSession(long userId)
        {
            var session = _userSessions.GetOrAdd(userId, id => {
                var newSession = new UserSession(id);
                newSession.Profile = _userDataStorageService.LoadProfile(id);
                if (newSession.TestHistory == null) newSession.TestHistory = new List<TestHistoryEntry>();
                if (newSession.LastShownLessonTitles == null) newSession.LastShownLessonTitles = new List<string>();
                if (newSession.LastShownTestList == null) newSession.LastShownTestList = new List<TestData>();
                return newSession;
            });

            if (session.Profile == null)
            {
                 session.Profile = _userDataStorageService.LoadProfile(userId);
            }
            if(session.Profile.UserId == 0 && userId != 0)
            {
                session.Profile.UserId = userId;
            }
            if (session.TestHistory == null)
            {
                session.TestHistory = new List<TestHistoryEntry>();
            }
            if (session.LastShownLessonTitles == null)
            {
                session.LastShownLessonTitles = new List<string>();
            }
            if (session.LastShownTestList == null)
            {
                session.LastShownTestList = new List<TestData>();
            }
            return session;
        }

        public void UpdateUserAuthentication(long userId, bool isAuthenticated)
        {
            var session = GetUserSession(userId);
            session.IsAuthenticated = isAuthenticated;
        }

        public void EndUserTest(long userId)
        {
            var session = GetUserSession(userId);
            session.EndCurrentTest();
        }

        public void UpdateProgress(long userId, int correctAnswersInLastTest)
        {
            var session = GetUserSession(userId);
            session.Profile.TotalTestsTaken += 1;
            session.Profile.TotalCorrectAnswers += correctAnswersInLastTest;
            _userDataStorageService.SaveProfile(session.Profile);
            Console.WriteLine($"Progress updated for UserId {userId}. TotalTests: {session.Profile.TotalTestsTaken}, TotalCorrect: {session.Profile.TotalCorrectAnswers}");
        }

        public void PersistUpdatedProfile(long userId)
        {
            var session = GetUserSession(userId);
            _userDataStorageService.SaveProfile(session.Profile);
            Console.WriteLine($"Profile explicitly persisted for UserId {userId} due to update (e.g., name change).");
        }

        public List<UserProfile> GetAllUserProfiles()
        {
            return _userDataStorageService.LoadAllProfiles();
        }

        public void LoadUsers(List<UserProfile> usersToLoad)
        {
            if (usersToLoad == null)
            {
                Console.WriteLine("LoadUsers called with null list. No action taken.");
                return;
            }

            Console.WriteLine($"Starting user data import. {usersToLoad.Count} users to load.");

            // 1. Clear existing user profile files
            // We need the storage directory path. UserDataStorageService keeps it private.
            // For now, we reconstruct it based on its default logic.
            // A better solution might be a method in UserDataStorageService to clear all data.
            string storageDirectory;
            try
            {
                string persistenceFolderPathBase = Path.Combine(AppContext.BaseDirectory, "user_data");
                // Ensure the directory variable matches what UserDataStorageService uses.
                // UserDataStorageService constructor: storageFolderName = "user_data"
                // _storageDirectory = Path.Combine(baseDirectory, storageFolderName);
                storageDirectory = persistenceFolderPathBase;


                if (Directory.Exists(storageDirectory))
                {
                    var existingProfileFiles = Directory.GetFiles(storageDirectory, "*_profile.json");
                    Console.WriteLine($"Found {existingProfileFiles.Length} existing profile files to delete in {storageDirectory}.");
                    foreach (var filePath in existingProfileFiles)
                    {
                        try
                        {
                            File.Delete(filePath);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error deleting existing profile file {filePath}: {ex.Message}");
                            // Continue to delete others if possible
                        }
                    }
                    Console.WriteLine("Finished deleting existing profile files.");
                }
                else
                {
                    Console.WriteLine($"Storage directory {storageDirectory} not found. No existing profiles to delete.");
                    // Ensure it exists for saving new profiles, UserDataStorageService will do this.
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error accessing or cleaning storage directory for user profiles: {ex.Message}");
                // Potentially abort the load if cleaning fails critically
                // For now, we'll proceed to try saving new profiles
            }


            // 2. Save each imported UserProfile
            foreach (var userProfile in usersToLoad)
            {
                if (userProfile != null)
                {
                    // Ensure UserId is set, as UserDataStorageService.SaveProfile uses it for filename
                    if (userProfile.UserId == 0) {
                        Console.WriteLine($"Warning: Importing a UserProfile with UserId 0. This profile might not be correctly saved or loaded by ID later. Name: {userProfile.Name}");
                        // Assign a temporary new ID if necessary, or skip? For now, save as is.
                    }
                    _userDataStorageService.SaveProfile(userProfile);
                }
            }
            Console.WriteLine($"Finished saving {usersToLoad.Count} imported user profiles.");

            // 3. Clear the in-memory _userSessions cache
            // This ensures that GetUserSession will reload from the new files.
            _userSessions.Clear();
            Console.WriteLine("In-memory user session cache cleared. Sessions will be reloaded on demand.");

            Console.WriteLine("User data import process complete.");
        }
    }
}
