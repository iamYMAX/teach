using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Omnieye.Bot.States; // For UserSession

namespace Omnieye.Bot.Services
{
    public class UserSessionService
    {
        private readonly string _persistenceFilePath; // For the main user_sessions.json
        private ConcurrentDictionary<long, UserSession> _userSessions; // Key: Telegram User ID
        private readonly UserDataStorageService _userDataStorageService; // For individual profiles

        private const string DefaultPersistenceFileName = "user_sessions.json";

        public UserSessionService(string persistenceFolderPath = "")
        {
            _userDataStorageService = new UserDataStorageService(); // Assumes default "user_data" subfolder

            if (string.IsNullOrWhiteSpace(persistenceFolderPath))
            {
                // Default to a 'data' subfolder in the application's base directory
                persistenceFolderPath = Path.Combine(AppContext.BaseDirectory, "data");
                Directory.CreateDirectory(persistenceFolderPath); // Ensure it exists
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
            // GetOrAdd will create a new UserSession(id) if not present.
            // The UserSession constructor already initializes Profile = new UserProfile(id).
            var session = _userSessions.GetOrAdd(userId, id => new UserSession(id));

            // Always ensure the profile is loaded/refreshed from UserDataStorageService.
            // This makes the individual profile file the source of truth for profile data.
            // UserSession's constructor will have created a default Profile; LoadProfile will overwrite it if a file exists.
            session.Profile = _userDataStorageService.LoadProfile(userId);
            if(session.Profile.UserId == 0 && userId != 0) // Ensure UserId is set in profile if loaded from an old file without it
            {
                session.Profile.UserId = userId;
            }


            return session;
        }

        // Example of how to update a session property, e.g., authentication
        public void UpdateUserAuthentication(long userId, bool isAuthenticated)
        {
            var session = GetUserSession(userId);
            session.IsAuthenticated = isAuthenticated;
            // No need to call _userSessions.TryUpdate as GetOrAdd returns the instance from the dictionary
        }

        // More methods will be added here to interact with UserSession properties like CurrentTestState
        // For example:
        // public void StartUserTest(long userId, Models.Test test) // OLD SYSTEM - DEPRECATED
        // {
        //     var session = GetUserSession(userId);
        //     // session.StartTest(test); // UserSession no longer has StartTest(Models.Test test)
        // }

        public void EndUserTest(long userId)
        {
            var session = GetUserSession(userId);
            // session.EndTest(); // This was for the old test system state.
            session.EndCurrentTest(); // This is for the new test system state.
        }

        public void UpdateProgress(long userId, int correctAnswersInLastTest) // totalQuestionsInLastTest is not needed if just incrementing
        {
            var session = GetUserSession(userId); // This ensures profile is loaded
            session.Profile.TotalTestsTaken += 1;
            session.Profile.TotalCorrectAnswers += correctAnswersInLastTest;

            _userDataStorageService.SaveProfile(session.Profile); // Persist updated profile
            Console.WriteLine($"Progress updated for UserId {userId}. TotalTests: {session.Profile.TotalTestsTaken}, TotalCorrect: {session.Profile.TotalCorrectAnswers}");
        }

        public void PersistUpdatedProfile(long userId) // Used after /setname
        {
            var session = GetUserSession(userId); // Ensure profile is loaded/exists in session
            _userDataStorageService.SaveProfile(session.Profile);
            Console.WriteLine($"Profile explicitly persisted for UserId {userId} due to update (e.g., name change).");
        }
    }
}
