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
        private readonly string _persistenceFilePath;
        private ConcurrentDictionary<long, UserSession> _userSessions; // Key: Telegram User ID

        private const string DefaultPersistenceFileName = "user_sessions.json";

        public UserSessionService(string persistenceFolderPath = "")
        {
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
            return _userSessions.GetOrAdd(userId, id => new UserSession(id));
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
    }
}
