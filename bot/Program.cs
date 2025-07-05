using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Omnieye.Bot.CoreModels;
using Omnieye.Bot.Services;    // Existing UserSessionService
using Omnieye.Bot.States;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Text.Json;
using Omnieye.Bot.Models;
using System.Timers;
using IOFile = System.IO.File;

// New using statements for the refactored course navigation
using OmnieyeBot.BotHandlers; // For MessageHandler (new)
using OmnieyeBot.Services;    // For CourseService (new)
// OmnieyeBot.Models are used by CourseService etc.

namespace Omnieye.Bot // Matching existing file's namespace
{
    class Program
    {
        // Existing services
        private static UserSessionService _userSessionService = new UserSessionService(); // Existing
        private static MaterialLoader _materialLoader = new MaterialLoader();
        private static TestLoaderService _testLoaderService = new TestLoaderService();

        // New services and handlers for course navigation
        private static CourseService _courseService; // New
        private static MessageHandler _courseMessageHandler; // New MessageHandler instance

        private static ITelegramBotClient? _botClient;
        private static CancellationTokenSource? _cts;

        // --- Existing static fields for keyboards, lessons, tests data (keeping them for now) ---
        private static readonly List<string> availableLessons_OLD_FORMAT = new List<string> { /* ... */ };
        private static readonly Dictionary<int, string> lessonDetails_OLD_FORMAT = new Dictionary<int, string> { /* ... */ };
        private static readonly List<string> availableTests_OLD_FORMAT = new List<string> { /* ... */ };
        private static readonly Dictionary<int, string> testDetails = new Dictionary<int, string> { /* ... */ };
        private static readonly ReplyKeyboardMarkup MainCommandKeyboard = new ReplyKeyboardMarkup(new[] { /* ... */ }) { ResizeKeyboard = true };
        private static readonly ReplyKeyboardMarkup LessonDetailKeyboard = new ReplyKeyboardMarkup(new[] { /* ... */ }) { ResizeKeyboard = true };
        private static readonly ReplyKeyboardMarkup TestDetailKeyboard = new ReplyKeyboardMarkup(new[] { /* ... */ }) { ResizeKeyboard = true };
        private static readonly ReplyKeyboardMarkup AfterTestMenuKeyboard = new ReplyKeyboardMarkup(new[] { /* ... */ }) { ResizeKeyboard = true, OneTimeKeyboard = true };
        private static readonly ReplyKeyboardMarkup FlashcardQuestionKeyboard = new ReplyKeyboardMarkup(new[] { /* ... */ }) { ResizeKeyboard = true };
        private static readonly Dictionary<int, TestData> activeTestsData = new Dictionary<int, TestData> { /* ... */ };
        private static readonly List<Lesson> allLessonsData = new List<Lesson> { /* ... */ }; // This is Omnieye.Bot.CoreModels.Lesson
        private static readonly List<Flashcard> allFlashcardsData = new List<Flashcard> { /* ... */ };
        // --- End of existing static fields ---


        static async Task Main(string[] args)
        {
            Console.WriteLine("Omnieye Telegram Bot starting...");
            var botToken = Environment.GetEnvironmentVariable("OMNIEYE_BOT_TOKEN") ?? "YOUR_BOT_TOKEN_HERE";
            if (botToken == "YOUR_BOT_TOKEN_HERE")
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("CRITICAL: Bot token is not set. Please set the OMNIEYE_BOT_TOKEN environment variable or replace the placeholder in code.");
                Console.ResetColor();
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
                return;
            }

            _botClient = new TelegramBotClient(botToken);
            _cts = new CancellationTokenSource();

            // Initialize new CourseService
            _courseService = new CourseService(); // Uses OmnieyeBot.Models.CourseModule/Lesson

            // Initialize new MessageHandler for course navigation
            // It needs botClient, new courseService, and existing userSessionService
            _courseMessageHandler = new MessageHandler(_botClient, _courseService, _userSessionService);


            // Start services like auto-backup
            StartAutoBackup();

            var receiverOptions = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };

            _botClient.StartReceiving(
                updateHandler: async (client, update, cancellationToken) =>
                {
                    // Try handling with the new course navigation logic first
                    bool handledByCourseLogic = await _courseMessageHandler.HandleUpdateAsync(client, update, cancellationToken);

                    // If not handled by course logic, pass to the original generic handler
                    if (!handledByCourseLogic)
                    {
                        await HandleUpdateAsyncOriginal(client, update, cancellationToken);
                    }
                },
                pollingErrorHandler: HandlePollingErrorAsync, // Existing error handler
                receiverOptions: receiverOptions,
                cancellationToken: _cts.Token);

            var me = await _botClient.GetMeAsync();
            Console.WriteLine($"Bot @{me.Username} started and listening for messages. Press Ctrl+C to exit.");

            var tcs = new TaskCompletionSource<bool>();
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                _cts.Cancel();
                tcs.SetResult(true);
                Console.WriteLine("Ctrl+C pressed. Shutting down...");
            };
            await tcs.Task;

            Console.WriteLine("Bot shutting down gracefully...");
            await _userSessionService.SaveSessionsToFileAsync();
            Console.WriteLine("Sessions saved. Exiting.");
        }

        // Renamed original HandleUpdateAsync to avoid conflict and indicate its role
        static async Task HandleUpdateAsyncOriginal(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Message is not { } message) return;
            if (message.From is not { } user) return;
            // Allow non-text messages for original handler if it supports them (e.g., photo for admin)
            // but new logic only handles text, so this check is fine here.
            if (message.Text is not { } messageText) return;


            long userId = user.Id;
            long chatId = message.Chat.Id;
            var session = _userSessionService.GetUserSession(userId); // Omnieye.Bot.States.UserSession

            Console.WriteLine($"Original Handler: Received '{messageText}' from User {userId} in Chat {chatId}. State: {session.CurrentState}, WaitingForName: {session.WaitingForNameInput}");

            // ... (The entire content of the original HandleUpdateAsync method goes here) ...
            // This includes:
            // - WaitingForNameInput logic
            // - TakingTest state logic
            // - Flashcard handling logic
            // - Authenticated button handling (switch statement for "📘 Уроки", "🧪 Тесты", etc. BUT "📘 Уроки" is now handled by new logic)
            // - Numeric input handling for lesson/test selection
            // - Command processing (switch statement for /login, /logout, /start, etc.)

            // IMPORTANT MODIFICATION:
            // The original switch (messageText) for main menu buttons needs to be aware that "📘 Уроки"
            // is now handled by the new system. If it's still there, it might lead to double handling or conflicts.
            // For now, I will assume the new logic fully takes over "📘 Уроки" and related "➡️ Модуль", "➡️ Урок", "⬅️ Назад" when in course context.
            // The original handler should NOT process these if they were meant for the new course navigation.
            // The boolean `handledByCourseLogic` from `Main` already prevents this method from running if those were matched.

            // For brevity, I'm not pasting the entire original HandleUpdateAsync here.
            // Assume it's the same as loaded, but it will only be called if the new courseMessageHandler didn't handle the update.
            // A key consideration is if the original `HandleUpdateAsyncOriginal` also had a "📘 Уроки" case.
            // If so, it's now effectively superseded if the message matches the new course handler's triggers.

            // Placeholder for the original HandleUpdateAsync's content
            if (session.WaitingForNameInput)
            {
                if (messageText.StartsWith("/"))
                {
                    session.WaitingForNameInput = false;
                    session.CurrentState = UserCurrentState.MainMenu;
                    await botClient.SendTextMessageAsync(chatId, "Ввод имени отменен.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                    if (messageText.ToLower() == "/setname") return;
                }
                else
                {
                    session.Profile.Name = messageText.Trim();
                    session.WaitingForNameInput = false;
                    _userSessionService.PersistUpdatedProfile(userId);

                    await botClient.SendTextMessageAsync(
                        chatId,
                        $"Имя сохранено как *{session.Profile.Name}*.",
                        parseMode: ParseMode.Markdown,
                        replyMarkup: MainCommandKeyboard,
                        cancellationToken: cancellationToken);

                    session.CurrentState = UserCurrentState.MainMenu;
                    return;
                }
            }

            if (session.CurrentState == UserCurrentState.TakingTest)
            {
                // Test taking logic... (abbreviated)
                if (!session.ActiveTestId.HasValue || !activeTestsData.TryGetValue(session.ActiveTestId.Value, out var currentTestData) ||
                    session.CurrentQuestionIndex >= currentTestData.Questions.Count)
                {
                    await botClient.SendTextMessageAsync(chatId, "Произошла ошибка с текущим тестом. Возвращаемся в главное меню.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                    session.EndCurrentTest();
                    return;
                }
                // ... rest of test logic
                 QuestionData currentQuestion = currentTestData.Questions[session.CurrentQuestionIndex];
                int selectedOptionIdx = currentQuestion.Options.IndexOf(messageText);

                if (selectedOptionIdx != -1)
                {
                    if (selectedOptionIdx == currentQuestion.CorrectOptionIndex) session.CurrentTestScore++;
                    session.CurrentQuestionIndex++;

                    if (session.CurrentQuestionIndex < currentTestData.Questions.Count)
                    {
                        await DisplayCurrentTestQuestionAsync(botClient, session, chatId, cancellationToken);
                    }
                    else
                    {
                        if (session.ActiveTestId.HasValue)
                        {
                            var historyEntry = new TestHistoryEntry
                            {
                                TestId = session.ActiveTestId.Value, TestTitle = currentTestData.TestName,
                                PassedAt = DateTime.UtcNow, TotalQuestions = currentTestData.Questions.Count,
                                CorrectAnswers = session.CurrentTestScore
                            };
                            session.TestHistory.Add(historyEntry);
                        }
                        _userSessionService.UpdateProgress(userId, session.CurrentTestScore);
                        string resultMessage = $"Тест \"{currentTestData.TestName}\" завершён.\nВаш результат: {session.CurrentTestScore} из {currentTestData.Questions.Count}.";
                        await botClient.SendTextMessageAsync(chatId, resultMessage, replyMarkup: AfterTestMenuKeyboard, cancellationToken: cancellationToken);
                        session.EndCurrentTest();
                    }
                }
                else if (messageText.ToLower() == "/stoptest")
                {
                    await HandleStopTestCommandAsync(botClient, session, chatId, cancellationToken);
                }
                else
                {
                    await botClient.SendTextMessageAsync(chatId, "Пожалуйста, выберите один из предложенных вариантов.", cancellationToken: cancellationToken);
                    await DisplayCurrentTestQuestionAsync(botClient, session, chatId, cancellationToken);
                }
                return;
            }

            if (session.CurrentState == UserCurrentState.ReviewingFlashcards)
            {
                bool flashcardActionProcessed = true;
                switch (messageText)
                {
                    case "Показать ответ": /* ... */ break;
                    case "Следующая карточка": await ShowNextFlashcardAsync(botClient, session, chatId, cancellationToken); break;
                    case "↩ Меню":
                        session.EndFlashcardSession();
                        await HandleStartCommandAsync(botClient, session, chatId, cancellationToken);
                        break;
                    default: flashcardActionProcessed = false; break;
                }
                if (flashcardActionProcessed) return;
            }

            if (session.IsAuthenticated)
            {
                bool keyboardButtonProcessed = true;
                // IMPORTANT: "📘 Уроки" is handled by the new system.
                // This switch should not re-process it. The chain of responsibility handles this.
                switch (messageText)
                {
                    // case "📘 Уроки": /* This is now handled by _courseMessageHandler */ break;
                    case "🧪 Тесты": await HandleTestsListAsync(botClient, session, chatId, cancellationToken); break;
                    case "🧠 Флеш-карточки": await StartFlashcardSessionAsync(botClient, session, chatId, cancellationToken); break;
                    case "История": await HandleHistoryAsync(botClient, session, chatId, cancellationToken); break;
                    case "👤 Профиль": await HandleProfileAsync(botClient, session, chatId, cancellationToken); break;
                    case "🏆 Топ": await ShowLeaderboardAsync(botClient, session, chatId, cancellationToken); break;
                    case "Назад":
                        if (session.CurrentState == UserCurrentState.ViewingTestDetail) await HandleTestsListAsync(botClient, session, chatId, cancellationToken);
                        else { /* ... */ }
                        break;
                    case "Назад к списку уроков": await HandleLessonsListAsync(botClient, session, chatId, cancellationToken); break;
                    case "Начать тест": /* ... */ break;
                    case "Вернуться в меню": await HandleStartCommandAsync(botClient, session, chatId, cancellationToken); session.CurrentState = UserCurrentState.MainMenu; break;
                    case "🔐 Выйти": await HandleLogoutCommandAsync(botClient, session, chatId, cancellationToken); break;
                    default: keyboardButtonProcessed = false; break;
                }
                if (keyboardButtonProcessed) return;
            }
            // ... rest of original input handling and command processing ...
            var parts = messageText.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLower();
            var argument = parts.Length > 1 ? parts[1] : null;

            if (!session.IsAuthenticated && command != "/login" && command != "/start" && command != "/help")
            {
                await botClient.SendTextMessageAsync(chatId, "You are not authenticated. Please use /login <password> to authenticate.", cancellationToken: cancellationToken);
                return;
            }

            try
            {
                switch (command)
                {
                    case "/login": await HandleLoginCommandAsync(botClient, session, chatId, argument, cancellationToken); break;
                    case "/logout": await HandleLogoutCommandAsync(botClient, session, chatId, cancellationToken); break;
                    case "/start": await HandleStartCommandAsync(botClient, session, chatId, cancellationToken); break;
                    case "/help": await HandleHelpCommandAsync(botClient, session, chatId, cancellationToken); break;
                    // case "/courses": await HandleCoursesCommandAsync(botClient, session, chatId, cancellationToken); break; // Superseded by "📘 Уроки" button
                    // case "/lesson": await HandleLessonCommandAsync(botClient, session, chatId, argument, cancellationToken); break; // Superseded by "➡️ Урок" button
                    case "/test": await HandleTestCommandAsync(botClient, session, chatId, argument, cancellationToken); break;
                    case "/stoptest": await HandleStopTestCommandAsync(botClient, session, chatId, cancellationToken); break;
                    case "/setname": /* ... */ break;
                    case "/profile": await HandleProfileAsync(botClient, session, chatId, cancellationToken); break;
                    case "/history": await HandleHistoryAsync(botClient, session, chatId, cancellationToken); break;
                    case "/leaderboard":
                    case "/top": await ShowLeaderboardAsync(botClient, session, chatId, cancellationToken); break;
                    case "/export": /* ... */ break;
                    case "/import": /* ... */ break;
                    default:
                        // If the new course logic didn't handle it, and it's not a known original command, then it's unknown.
                        await botClient.SendTextMessageAsync(chatId, $"Unknown command '{command}'. Try /help for commands.", cancellationToken: cancellationToken);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing command '{command}' for user {userId}: {ex}");
                await botClient.SendTextMessageAsync(chatId, "An error occurred while processing your request.", cancellationToken: cancellationToken);
            }
        }

        // --- Keep all other existing static methods from Program.cs ---
        // HandlePollingErrorAsync, ExportDataAsync, IsAdmin, PerformAutoBackupAsync, StartAutoBackup,
        // ImportDataAsync, HandleLoginCommandAsync, HandleLogoutCommandAsync, HandleStartCommandAsync,
        // HandleHelpCommandAsync, HandleCoursesCommandAsync, HandleLessonCommandAsync, HandleTestCommandAsync,
        // GetAvailableLessonsForUserLevel, GetLessonDifficultyIcon, GetDifficultyIcon, HandleLessonsListAsync,
        // HandleLessonContentAsync, HandleTestsListAsync, HandleTestDetailAsync, StartActualTestAsync,
        // DisplayCurrentTestQuestionAsync, HandleStopTestCommandAsync, HandleHistoryAsync, HandleProfileAsync,
        // SendLongMessageAsync, SendCombinedMessages, ShowLeaderboardAsync, SplitMessage,
        // ShowNextFlashcardAsync, StartFlashcardSessionAsync (and its helper GetFlashcardsByLevel)
        // --- They are not being changed in this step, only the main update path ---
        static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken) { /* ... original content ... */ var ErrorMessage = exception switch { ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}", _ => exception.ToString() }; Console.WriteLine(ErrorMessage); return Task.CompletedTask; }
        private static async Task ExportDataAsync() { /* ... original content ... */ }
        private static bool IsAdmin(long userId) { /* ... original content ... */ return false; } // Simplified for brevity
        private static async Task PerformAutoBackupAsync() { /* ... original content ... */ }
        private static void StartAutoBackup() { /* ... original content ... */ }
        private static async Task ImportDataAsync() { /* ... original content ... */ }
        static async Task HandleLoginCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, string? password, CancellationToken ct) { /* ... original content ... */ }
        static async Task HandleLogoutCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct) { /* ... original content ... */ }
        static async Task HandleStartCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct) { /* ... original content ... */
             if (session.IsAuthenticated) { await botClient.SendTextMessageAsync(chatId, "Главное меню.", replyMarkup: MainCommandKeyboard, cancellationToken: ct); session.CurrentState = UserCurrentState.MainMenu; }
             else { await botClient.SendTextMessageAsync(chatId, "Welcome. Use /login <password>", cancellationToken: ct); }
        }
        static async Task HandleHelpCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct) { /* ... original content ... */ }
        // HandleCoursesCommandAsync and HandleLessonCommandAsync are effectively replaced by the new UI
        // static async Task HandleCoursesCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct) { /* ... */ }
        // static async Task HandleLessonCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, string? argument, CancellationToken ct) { /* ... */ }
        static async Task HandleTestCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, string? argument, CancellationToken ct) { /* ... original content ... */ }
        static List<Lesson> GetAvailableLessonsForUserLevel(int userProfileLevel, IEnumerable<Lesson> allLessons) { /* ... original content ... */ return allLessons.ToList(); }
        static string GetLessonDifficultyIcon(LessonLevel level) { /* ... original content ... */ return ""; }
        static string GetDifficultyIcon(TestDifficulty difficulty) { /* ... original content ... */ return ""; }
        static async Task HandleLessonsListAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct) { /* ... original content ... */ }
        static async Task HandleLessonContentAsync(ITelegramBotClient botClient, UserSession session, long chatId, Lesson lessonToShow, CancellationToken ct) { /* ... original content ... */ }
        static async Task HandleTestsListAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct) { /* ... original content ... */ }
        static async Task HandleTestDetailAsync(ITelegramBotClient botClient, UserSession session, long chatId, int testId, CancellationToken ct) { /* ... original content ... */ }
        static async Task StartActualTestAsync(ITelegramBotClient botClient, UserSession session, long chatId, int testId, CancellationToken ct) { /* ... original content ... */ }
        static async Task DisplayCurrentTestQuestionAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct) { /* ... original content ... */ }
        static async Task HandleStopTestCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct) { /* ... original content ... */ }
        static async Task HandleHistoryAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct) { /* ... original content ... */ }
        static async Task HandleProfileAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct) { /* ... original content ... */ }
        static async Task SendLongMessageAsync(ITelegramBotClient botClient, long chatId, string message, CancellationToken cancellationToken, IReplyMarkup? replyMarkup = null, ParseMode? parseMode = null, int chunkSize = 4000) { /* ... original content ... */ if (string.IsNullOrEmpty(message)) return; await botClient.SendTextMessageAsync(chatId, message.Substring(0, Math.Min(message.Length, chunkSize)), replyMarkup: replyMarkup, parseMode: parseMode, cancellationToken: cancellationToken); }
        static async Task SendCombinedMessages(ITelegramBotClient botClient, long chatId, List<string> messages, CancellationToken cancellationToken, IReplyMarkup? replyMarkup = null, ParseMode? parseMode = null) { /* ... original content ... */ }
        static async Task ShowLeaderboardAsync(ITelegramBotClient botClient, UserSession currentSession, long chatId, CancellationToken ct) { /* ... original content ... */ }
        public static List<string> SplitMessage(string message, int chunkSize = 4000) { /* ... original content ... */ return new List<string>{message}; }
        static async Task ShowNextFlashcardAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct) { /* ... */ }
        static async Task StartFlashcardSessionAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct) { /* ... */ }
        static List<Flashcard> GetFlashcardsByLevel(int userProfileLevel, IEnumerable<Flashcard> allFlashcards) { /* ... */ return allFlashcards.ToList(); }

    } // End of Program class

    public static class AuthorizationService
    {
        // ... (original content) ...
        private const string HardcodedPassword = "omni_password123";
        public static bool Authenticate(long userId, string? password, UserSessionService sessionService) { if (password == HardcodedPassword) { sessionService.UpdateUserAuthentication(userId, true); return true; } return false; }
        public static bool CheckAuthentication(long userId, UserSessionService sessionService) { return sessionService.GetUserSession(userId).IsAuthenticated; }
        public static void Logout(long userId, UserSessionService sessionService) { sessionService.UpdateUserAuthentication(userId, false); sessionService.GetUserSession(userId).EndCurrentTest(); }
    }
}
