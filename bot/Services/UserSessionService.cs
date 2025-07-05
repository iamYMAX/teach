using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection; // Required for DefaultContractResolver BindingFlags
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization; // Required for DefaultContractResolver
using Omnieye.Bot.States; // For UserSession, TestHistoryEntry
using Omnieye.Bot.CoreModels; // For TestData used in GetUserSession

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
                persistenceFolderPath = Path.Combine(AppContext.BaseDirectory, "data");
                Directory.CreateDirectory(persistenceFolderPath);
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
                    var settings = new JsonSerializerSettings
                    {
                        // Ensure properties with private setters or non-public members can be handled if needed,
                        // though UserSession primarily uses public properties.
                        // Using DefaultContractResolver with appropriate flags can help with complex scenarios.
                        // For UserSession as it is now, default settings are likely fine.
                        // Adding this for robustness in case of future UserSession changes or complex types within.
                        ContractResolver = new DefaultContractResolver
                        {
                             DefaultMembersSearchFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                        },
                        ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor // If UserSession had a private constructor
                    };

                    var sessions = JsonConvert.DeserializeObject<ConcurrentDictionary<long, UserSession>>(jsonData); // Removed settings for now, rely on defaults
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
                                session.TestHistory = new System.Collections.Generic.List<TestHistoryEntry>();
                            }
                            if (session.LastShownLessonTitles == null) // Initialize if null after deserialization
                            {
                                session.LastShownLessonTitles = new System.Collections.Generic.List<string>();
                            }
                            if (session.LastShownTestList == null) // Initialize if null
                            {
                                session.LastShownTestList = new System.Collections.Generic.List<CoreModels.TestData>();
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
                // TestHistory, LastShownLessonTitles, LastShownTestList are initialized in UserSession constructor or here if needed
                if (newSession.TestHistory == null) newSession.TestHistory = new System.Collections.Generic.List<TestHistoryEntry>();
                if (newSession.LastShownLessonTitles == null) newSession.LastShownLessonTitles = new System.Collections.Generic.List<string>();
                if (newSession.LastShownTestList == null) newSession.LastShownTestList = new System.Collections.Generic.List<CoreModels.TestData>();
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
                session.TestHistory = new System.Collections.Generic.List<TestHistoryEntry>();
            }
            if (session.LastShownLessonTitles == null)
            {
                session.LastShownLessonTitles = new System.Collections.Generic.List<string>();
            }
            if (session.LastShownTestList == null)
            {
                session.LastShownTestList = new System.Collections.Generic.List<CoreModels.TestData>();
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
            // This method relies on UserDataStorageService to fetch all profiles
            // from their individual JSON files.
            return _userDataStorageService.LoadAllProfiles();
        }
    }
}
