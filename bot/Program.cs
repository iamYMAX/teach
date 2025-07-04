using System;
using System;
using System.Threading;
using System.Threading.Tasks;
using Omnieye.Bot.Models;
using Omnieye.Bot.Services;
using Omnieye.Bot.States;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups; // Required for ReplyKeyboardMarkup

namespace Omnieye.Bot
{
    class Program
    {
        private static TestLoaderService _testLoaderService = new TestLoaderService();
        private static UserSessionService _userSessionService = new UserSessionService();
        private static MaterialLoader _materialLoader = new MaterialLoader(); // Initialize MaterialLoader

        private static ITelegramBotClient? _botClient;
        private static CancellationTokenSource? _cts;

        private static readonly List<string> availableLessons = new List<string>
        {
            "Урок 1: Введение в систему",
            "Урок 2: Основы работы",
            "Урок 3: Продвинутые возможности"
        };

        private static readonly List<string> availableTests = new List<string>
        {
            "Тест 1: Проверка знаний по основам",
            "Тест 2: Продвинутый тест"
        };

        private static readonly Dictionary<int, string> lessonDetails = new Dictionary<int, string>
        {
            { 1, "Урок 1: Введение в систему\n\nЗдесь рассказывается об основах работы с ботом и системой." },
            { 2, "Урок 2: Основы работы\n\nОписание основных функций и интерфейса." },
            { 3, "Урок 3: Продвинутые возможности\n\nДополнительные настройки и советы." }
        };

        private static readonly Dictionary<int, string> testDetails = new Dictionary<int, string>
        {
            { 1, "Тест 1: Проверка знаний по основам\n\nВключает 10 вопросов по базовым темам." },
            { 2, "Тест 2: Продвинутый тест\n\nСложные вопросы для опытных пользователей." }
        };

        private static readonly ReplyKeyboardMarkup MainCommandKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new[] { new KeyboardButton("📘 Уроки"), new KeyboardButton("🧪 Тесты") },
            new[] { new KeyboardButton("🔐 Выйти") }
        })
        {
            ResizeKeyboard = true
        };

        private static readonly ReplyKeyboardMarkup LessonDetailKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "Назад" }
        })
        {
            ResizeKeyboard = true
        };

        private static readonly ReplyKeyboardMarkup TestDetailKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "Начать тест", "Назад" }
        })
        {
            ResizeKeyboard = true
        };

        // --- Test Data Structures ---
        public class QuestionData
        {
            public string Text { get; }
            public List<string> Options { get; }
            public int CorrectOptionIndex { get; }

            public QuestionData(string text, List<string> options, int correctOptionIndex)
            {
                Text = text;
                Options = options;
                CorrectOptionIndex = correctOptionIndex;
            }
        }

        public class TestData
        {
            public int TestId { get; }
            public string TestName { get; } // Added for potential use in messages
            public List<QuestionData> Questions { get; }

            public TestData(int testId, string testName, List<QuestionData> questions)
            {
                TestId = testId;
                TestName = testName;
                Questions = questions;
            }
        }

        private static readonly Dictionary<int, TestData> activeTestsData = new Dictionary<int, TestData>
        {
            {
                1, new TestData(1, "Тест по основам", new List<QuestionData>
                {
                    new QuestionData("Вопрос 1: Что такое бот?", new List<string>{ "Программа", "Человек", "Животное" }, 0),
                    new QuestionData("Вопрос 2: Какой язык используется в этом боте?", new List<string>{ "C#", "Python", "JavaScript" }, 0)
                })
            },
            {
                2, new TestData(2, "Продвинутый тест", new List<QuestionData>
                {
                    new QuestionData("Вопрос 1 (П): Что такое сеть?", new List<string>{ "Группа компьютеров", "Отдельный компьютер", "Принтер" }, 0),
                    new QuestionData("Вопрос 2 (П): IP-адрес это?", new List<string>{ "Физический адрес", "Логический адрес", "Почтовый адрес" }, 1)
                })
            }
        };
        // --- End Test Data Structures ---


        static async Task Main(string[] args)
        {
            Console.WriteLine("Omnieye Telegram Bot starting...");

            // IMPORTANT: Replace with your actual bot token from BotFather
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

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>() // receive all update types
            };

            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: _cts.Token
            );

            var me = await _botClient.GetMeAsync();
            Console.WriteLine($"Bot @{me.Username} started and listening for messages. Press Ctrl+C to exit.");

            // Keep the application running until Ctrl+C is pressed
            var tcs = new TaskCompletionSource<bool>();
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true; // Prevent immediate termination
                _cts.Cancel();          // Signal cancellation to the bot
                tcs.SetResult(true);    // Allow Main to continue and save sessions
                Console.WriteLine("Ctrl+C pressed. Shutting down...");
            };

            await tcs.Task; // Wait for Ctrl+C

            Console.WriteLine("Bot shutting down gracefully...");
            await _userSessionService.SaveSessionsToFileAsync(); // Save sessions on shutdown
            Console.WriteLine("Sessions saved. Exiting.");
        }

        static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Message is not { } message) return;
            if (message.From is not { } user) return; // Ensure sender is a user
            if (message.Text is not { } messageText) return;

            long userId = user.Id;
            long chatId = message.Chat.Id;

            Console.WriteLine($"Received a '{messageText}' message from User {userId} in chat {chatId}.");

            var session = _userSessionService.GetUserSession(userId);

            // If a test is active, treat input as an answer first
            if (session.CurrentState == UserCurrentState.TakingTest)
            {
                // Make sure ActiveTestId and current question are valid
                if (!session.ActiveTestId.HasValue || !activeTestsData.TryGetValue(session.ActiveTestId.Value, out var currentTestData) ||
                    session.CurrentQuestionIndex >= currentTestData.Questions.Count)
                {
                    // Invalid state, perhaps test ended abruptly or data error
                    await botClient.SendTextMessageAsync(chatId, "Произошла ошибка с текущим тестом. Возвращаемся в главное меню.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                    session.EndCurrentTest(); // Reset all test parameters
                    session.CurrentState = UserCurrentState.MainMenu;
                    return;
                }

                QuestionData currentQuestion = currentTestData.Questions[session.CurrentQuestionIndex];
                int selectedOptionIdx = currentQuestion.Options.IndexOf(messageText);

                if (selectedOptionIdx != -1) // User's message matches one of the options
                {
                    if (selectedOptionIdx == currentQuestion.CorrectOptionIndex)
                    {
                        session.CurrentTestScore++;
                    }
                    session.CurrentQuestionIndex++;

                    if (session.CurrentQuestionIndex < currentTestData.Questions.Count)
                    {
                        await DisplayCurrentTestQuestionAsync(botClient, session, chatId, cancellationToken);
                    }
                    else // Test finished
                    {
                        string resultMessage = $"Тест \"{currentTestData.TestName}\" завершён.\nВаш результат: {session.CurrentTestScore} из {currentTestData.Questions.Count}.";

                        // As per task: "Предлагать кнопку "Вернуться в меню" после окончания теста"
                        // This implies the MainCommandKeyboard might be better if "Вернуться в меню" is not a button itself,
                        // or we create a specific one. Let's use MainCommandKeyboard for now.
                        var menuKeyboard = new ReplyKeyboardMarkup(new[] { new KeyboardButton("Вернуться в меню") })
                        {
                            ResizeKeyboard = true,
                            OneTimeKeyboard = true
                        };

                        await botClient.SendTextMessageAsync(chatId, resultMessage, replyMarkup: menuKeyboard, cancellationToken: cancellationToken);

                        // session.EndCurrentTest(); // Reset test-specific fields
                        // session.CurrentState = UserCurrentState.MainMenu; // Set state after test. EndCurrentTest already sets it to MainMenu.
                        // Let's ensure EndCurrentTest is called and handles state properly.
                        // The task implies "Вернуться в меню" button handles the state transition.
                        // For now, just reset test fields. The next input ("Вернуться в меню") will handle state.
                        // Or, we can assume after results, they are implicitly in a post-test state awaiting "Вернуться в меню"
                        session.ActiveTestId = null; // Keep score and index for review if needed, but mark test as inactive
                                                     // The next "Вернуться в меню" will fully reset via EndCurrentTest or by setting MainMenu state.
                                                     // For simplicity, let's reset fully here and set to MainMenu.
                                                     // The button "Вернуться в меню" would then just be a trigger to show the main menu message.
                        session.EndCurrentTest(); // This will set state to MainMenu and clear test vars.

                    }
                }
                else if (messageText.ToLower() == "/stoptest") // Allow /stoptest during test
                {
                    await HandleStopTestCommandAsync(botClient, session, chatId, cancellationToken);
                }
                else
                {
                    // Input does not match any option, re-send the question or send an error
                    await botClient.SendTextMessageAsync(chatId, "Пожалуйста, выберите один из предложенных вариантов.", cancellationToken: cancellationToken);
                    // Re-display the current question with its keyboard
                    await DisplayCurrentTestQuestionAsync(botClient, session, chatId, cancellationToken);
                }
                return; // Input processed (or re-prompted) within test context
            }
            // Check for OLD test system state (UserTestState from Models/JSON) - this should be phased out
            else if (session.CurrentTestState != null && session.CurrentTestState.IsTestActive)
            {
                if (int.TryParse(messageText, out int answerOpt) && answerOpt > 0 &&
                    session.CurrentTestState.GetCurrentQuestion() != null &&
                    answerOpt <= session.CurrentTestState.GetCurrentQuestion().Options.Count)
                {
                    await HandleAnswerInputAsync(botClient, userId, chatId, answerOpt - 1, cancellationToken);
                    return;
                }
            }


            // Handle keyboard button presses first if user is authenticated
            if (session.IsAuthenticated)
            {
                bool keyboardButtonProcessed = true; // Assume it's a keyboard button initially
                switch (messageText)
                {
                    case "📘 Уроки":
                        await HandleLessonsListAsync(botClient, session, chatId, cancellationToken);
                        break;
                    case "🧪 Тесты":
                        await HandleTestsListAsync(botClient, session, chatId, cancellationToken);
                        break;
                    case "Назад": // "Back" button
                        if (session.CurrentState == UserCurrentState.ViewingLessonDetail)
                        {
                            await HandleLessonsListAsync(botClient, session, chatId, cancellationToken);
                            // HandleLessonsListAsync already sets state to ViewingLessonList
                        }
                        else if (session.CurrentState == UserCurrentState.ViewingTestDetail)
                        {
                            await HandleTestsListAsync(botClient, session, chatId, cancellationToken);
                            // HandleTestsListAsync already sets state to ViewingTestList
                        }
                        else
                        {
                            // If "Назад" is pressed from an unexpected state, default to Main Menu or send current keyboard
                            // For now, let's assume "Назад" always means go to the relevant list or main menu if no list context.
                            // But our "Назад" buttons are only on detail views, which should take to lists.
                            // If they are in ViewingLessonList or ViewingTestList and press a "Назад" (if it existed there)
                            // they'd go to MainMenu.
                            // For now, this case might not be hit if "Назад" is only on detail keyboards.
                            // If it is hit, sending MainCommandKeyboard is a safe fallback.
                             await botClient.SendTextMessageAsync(chatId, "Возвращаемся в главное меню.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                             session.CurrentState = UserCurrentState.MainMenu;
                        }
                        break;
                    case "Начать тест": // "Start Test" button
                        if (session.CurrentState == UserCurrentState.ViewingTestDetail && session.ViewingItemId.HasValue)
                        {
                            await StartActualTestAsync(botClient, session, chatId, session.ViewingItemId.Value, cancellationToken);
                        }
                        else if (session.CurrentState == UserCurrentState.ViewingTestDetail && !session.ViewingItemId.HasValue)
                        {
                             await botClient.SendTextMessageAsync(chatId, "Ошибка: не удалось определить, какой тест запустить. Пожалуйста, вернитесь к списку тестов.", replyMarkup: TestDetailKeyboard, cancellationToken: cancellationToken);
                        }
                        else
                        {
                            // If "Начать тест" is pressed from an unexpected state
                            await botClient.SendTextMessageAsync(chatId, "Пожалуйста, сначала выберите тест из списка.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                            // Optionally, call HandleTestsListAsync if you want to redirect them
                        }
                        break;
                    case "Вернуться в меню": // New button after test completion
                        // Ensure this is handled when the user is in a post-test state or a generic state
                        // HandleStartCommandAsync will show the main menu and appropriate keyboard
                        // UserSession.EndCurrentTest() already sets state to MainMenu.
                        // So, pressing this button when state is MainMenu should just re-trigger the /start message.
                        await HandleStartCommandAsync(botClient, session, chatId, cancellationToken);
                        // Ensure state is MainMenu if it wasn't already set by EndCurrentTest or if called from other contexts.
                        session.CurrentState = UserCurrentState.MainMenu;
                        break;
                    case "🔐 Выйти":
                        await HandleLogoutCommandAsync(botClient, session, chatId, cancellationToken);
                        break;
                    default:
                        keyboardButtonProcessed = false; // Not a recognized keyboard button text
                        break;
                }
                if (keyboardButtonProcessed) return; // If it was a keyboard button, we're done with this update
            }

            // Placeholder for selecting lesson/test by number after viewing lists
            // This is a simplified approach. A more robust solution might involve tracking user state (e.g., "justViewedLessonsList").
            if (session.IsAuthenticated && int.TryParse(messageText, out int selectionNumber) && selectionNumber > 0)
            {
                bool selectionHandled = false;
                if (session.CurrentState == UserCurrentState.ViewingLessonList)
                {
                    await HandleLessonDetailAsync(botClient, session, chatId, selectionNumber, cancellationToken);
                    selectionHandled = true;
                }
                else if (session.CurrentState == UserCurrentState.ViewingTestList)
                {
                    await HandleTestDetailAsync(botClient, session, chatId, selectionNumber, cancellationToken);
                    selectionHandled = true;
                }
                // Potentially add other states here if numeric input is expected elsewhere.

                if (selectionHandled)
                {
                    return; // Input was processed as a numeric selection for a list.
                }
                // If it was a number but not in a list-viewing state, or an invalid number for that list,
                // it will fall through. We might want to add a generic "Invalid selection" message here
                // if session.CurrentState was one of the list viewing states but the number was out of bounds.
                // For now, HandleLessonDetailAsync/HandleTestDetailAsync handle "not found".
            }

            var parts = messageText.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLower();
            var argument = parts.Length > 1 ? parts[1] : null;

            // Standard command authentication check (allow /start, /help, /login before auth)
            if (!session.IsAuthenticated && command != "/login" && command != "/start" && command != "/help")
            {
                await botClient.SendTextMessageAsync(chatId, "You are not authenticated. Please use /login <password> to authenticate.", cancellationToken: cancellationToken);
                return;
            }

            try
            {
                // Process standard commands if not a keyboard button
                switch (command)
                {
                    case "/login":
                        await HandleLoginCommandAsync(botClient, session, chatId, argument, cancellationToken);
                        break;
                    case "/logout":
                        await HandleLogoutCommandAsync(botClient, session, chatId, cancellationToken);
                        break;
                    case "/start":
                        await HandleStartCommandAsync(botClient, session, chatId, cancellationToken);
                        break;
                    case "/help":
                        await HandleHelpCommandAsync(botClient, session, chatId, cancellationToken);
                        break;
                    case "/courses":
                        await HandleCoursesCommandAsync(botClient, session, chatId, cancellationToken);
                        break;
                    case "/lesson":
                        await HandleLessonCommandAsync(botClient, session, chatId, argument, cancellationToken);
                        break;
                    case "/test":
                        await HandleTestCommandAsync(botClient, session, chatId, argument, cancellationToken);
                        break;
                    case "/stoptest":
                        await HandleStopTestCommandAsync(botClient, session, chatId, cancellationToken);
                        break;
                    default:
                        if (session.CurrentTestState != null && session.CurrentTestState.IsTestActive) {
                             await botClient.SendTextMessageAsync(chatId, $"Invalid input. Please enter the number of your answer or use /stoptest.", cancellationToken: cancellationToken);
                        } else {
                             await botClient.SendTextMessageAsync(chatId, $"Unknown command '{command}'. Try /help for commands.", cancellationToken: cancellationToken);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing command '{command}' for user {userId}: {ex}");
                await botClient.SendTextMessageAsync(chatId, "An error occurred while processing your request. Please try again later.", cancellationToken: cancellationToken);
            }
        }

        static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };
            Console.WriteLine(ErrorMessage);
            return Task.CompletedTask;
        }

        // --- Command Handlers adapted for async and Telegram ---

        static async Task HandleLoginCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, string? password, CancellationToken ct)
        {
            if (session.CurrentTestState != null && session.CurrentTestState.IsTestActive) {
                await botClient.SendTextMessageAsync(chatId, "Please finish or stop the current test (/stoptest) before logging in again.", cancellationToken: ct);
                return;
            }
            if (session.IsAuthenticated) {
                await botClient.SendTextMessageAsync(chatId, "You are already authenticated.", cancellationToken: ct);
                return;
            }
            if (string.IsNullOrWhiteSpace(password)) {
                await botClient.SendTextMessageAsync(chatId, "Please provide a password. Usage: /login <password>", cancellationToken: ct);
                return;
            }
            if (AuthorizationService.Authenticate(session.UserId, password, _userSessionService)) {
                await botClient.SendTextMessageAsync(
                    chatId,
                    "Вы успешно авторизованы.\nВыберите действие:",
                    replyMarkup: MainCommandKeyboard,
                    cancellationToken: ct);
            } else {
                await botClient.SendTextMessageAsync(chatId, "Authentication failed. Invalid password.", cancellationToken: ct);
            }
        }

        static async Task HandleLogoutCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentTestState != null && session.CurrentTestState.IsTestActive) {
                await botClient.SendTextMessageAsync(chatId, "Please finish or stop the current test (/stoptest) before logging out.", cancellationToken: ct);
                return;
            }
            if (!session.IsAuthenticated) {
                 await botClient.SendTextMessageAsync(chatId, "You are not currently authenticated.", cancellationToken: ct);
                return;
            }
            AuthorizationService.Logout(session.UserId, _userSessionService); // This already calls EndUserTest
            session.CurrentState = UserCurrentState.MainMenu; // Explicitly reset navigation state
            // ViewingItemId will be naturally irrelevant once state is MainMenu or will be overwritten on next valid navigation.

            await botClient.SendTextMessageAsync(
                chatId,
                "You have been logged out.",
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: ct);
        }


        static async Task HandleStartCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            var messages = new System.Collections.Generic.List<string>
            {
                "Welcome to Omnieye Certification Bot! Use /courses to see available courses, or /help for more commands."
            };
            if (!session.IsAuthenticated)
            {
                messages.Add("Please use /login <password> to access content.");
            }
            if (session.CurrentTestState != null && session.CurrentTestState.IsTestActive)
            {
                messages.Add("You are currently in a test. Enter an answer or use /stoptest.");
                await SendCombinedMessages(botClient, chatId, messages, ct);
                await DisplayCurrentQuestionAsync(botClient, session, chatId, ct);
            }
            else // Not in an active test
            {
                // The messages list already contains the base welcome and login prompt if applicable.
                // Now decide which keyboard to send.
                if (session.IsAuthenticated)
                {
                    await SendCombinedMessages(botClient, chatId, messages, ct, MainCommandKeyboard);
                }
                else // Not authenticated and not in a test
                {
                    await SendCombinedMessages(botClient, chatId, messages, ct);
                }
            }
        }

        static async Task HandleHelpCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            var messages = new System.Collections.Generic.List<string>();
            messages.Add("Available commands:");

            if (session.CurrentTestState != null && session.CurrentTestState.IsTestActive)
            {
                messages.Add("You are currently in a test.");
                messages.Add("Enter the number of your chosen option to answer.");
                messages.Add("/stoptest - Stop the current test.");
            }
            else if (!session.IsAuthenticated)
            {
                messages.Add("/login <password> - Authenticate to access the bot");
                messages.Add("/start - Welcome message");
            }
            else
            {
                messages.Add("/logout - Log out from the bot");
                messages.Add("/courses - List available courses");
                messages.Add("/lesson <number> - Get lesson content");
                messages.Add("/test <lesson_number_or_id> - Start a test for a lesson/topic");
                messages.Add("/stoptest - If you are in a test, this will stop it.");
                messages.Add("/start - Welcome message");
            }
            messages.Add("/help - Show this help message");
            await SendCombinedMessages(botClient, chatId, messages, ct);
        }

        static async Task HandleCoursesCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentTestState != null && session.CurrentTestState.IsTestActive) {
                await botClient.SendTextMessageAsync(chatId, "Please finish or stop the current test (/stoptest) before viewing courses.", cancellationToken: ct);
                await DisplayCurrentQuestionAsync(botClient, session, chatId, ct);
                return;
            }
            await botClient.SendTextMessageAsync(chatId, "Available courses: Junior Admin (More coming soon!)", cancellationToken: ct);
        }

        static async Task HandleLessonCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, string? argument, CancellationToken ct)
        {
            if (session.CurrentTestState != null && session.CurrentTestState.IsTestActive) {
                await botClient.SendTextMessageAsync(chatId, "Please finish or stop the current test (/stoptest) before starting a lesson.", cancellationToken: ct);
                await DisplayCurrentQuestionAsync(botClient, session, chatId, ct);
                return;
            }

            if (string.IsNullOrWhiteSpace(argument) || !int.TryParse(argument, out int lessonNumber) || lessonNumber <= 0) {
                await botClient.SendTextMessageAsync(chatId, "Please provide a valid lesson number. Usage: /lesson <number>", cancellationToken: ct);
                return;
            }

            string? theory = _materialLoader.LoadTheory(lessonNumber);
            string? practice = _materialLoader.LoadPractice(lessonNumber);

            if (theory == null && practice == null) {
                await botClient.SendTextMessageAsync(chatId, $"Lesson {lessonNumber} not found.", cancellationToken: ct);
                return;
            }

            await botClient.SendTextMessageAsync(chatId, $"--- Lesson {lessonNumber} ---", cancellationToken: ct);
            if (theory != null) {
                await SendLongMessageAsync(botClient, chatId, $"--- Theory ---\n{theory}", ct);
            } else {
                await botClient.SendTextMessageAsync(chatId, "--- Theory: Not available for this lesson. ---", cancellationToken: ct);
            }
            if (practice != null) {
                 await SendLongMessageAsync(botClient, chatId, $"--- Practice ---\n{practice}", ct);
            } else {
                 await botClient.SendTextMessageAsync(chatId, "--- Practice: Not available for this lesson. ---", cancellationToken: ct);
            }
            await botClient.SendTextMessageAsync(chatId, $"--- End of Lesson {lessonNumber} ---", cancellationToken: ct);
        }

        static async Task HandleTestCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, string? argument, CancellationToken ct)
        {
            if (session.CurrentTestState != null && session.CurrentTestState.IsTestActive) {
                await botClient.SendTextMessageAsync(chatId, "You are already in a test. Please complete it or use /stoptest.", cancellationToken: ct);
                await DisplayCurrentQuestionAsync(botClient, session, chatId, ct);
                return;
            }

            Test? testData = _testLoaderService.LoadTest();
            if (testData == null || testData.Questions.Count == 0) {
                await botClient.SendTextMessageAsync(chatId, "Sorry, the test is currently unavailable or has no questions.", cancellationToken: ct);
                return;
            }

            _userSessionService.StartUserTest(session.UserId, testData);
            // session = _userSessionService.GetUserSession(session.UserId); // Refresh, though StartUserTest modifies the instance
            await botClient.SendTextMessageAsync(chatId, $"Starting Test: {testData.CourseName}", cancellationToken: ct);
            await DisplayCurrentQuestionAsync(botClient, session, chatId, ct);
        }

        static async Task HandleAnswerInputAsync(ITelegramBotClient botClient, long userId, long chatId, int selectedOptionIndex, CancellationToken ct)
        {
            var session = _userSessionService.GetUserSession(userId); // Ensure we have the latest session state
            if (session.CurrentTestState == null || !session.CurrentTestState.IsTestActive) {
                await botClient.SendTextMessageAsync(chatId, "No active test. Use /test to start one.", cancellationToken: ct);
                return;
            }

            session.CurrentTestState.SubmitAnswer(selectedOptionIndex);

            if (session.CurrentTestState.IsTestActive) {
                await DisplayCurrentQuestionAsync(botClient, session, chatId, ct);
            } else {
                var (score, totalQuestions) = session.CurrentTestState.CalculateScore();
                await botClient.SendTextMessageAsync(chatId, $"Test Complete! Your score: {score} out of {totalQuestions}.", cancellationToken: ct);
                _userSessionService.EndUserTest(userId);
                await botClient.SendTextMessageAsync(chatId, "Type /help for more commands.", cancellationToken: ct);
            }
        }

        static async Task HandleTestDetailAsync(ITelegramBotClient botClient, UserSession session, long chatId, int testNumber, CancellationToken ct)
        {
            if (testDetails.TryGetValue(testNumber, out string? detailText))
            {
                await SendLongMessageAsync(botClient, chatId, detailText, ct, TestDetailKeyboard);
                session.CurrentState = UserCurrentState.ViewingTestDetail;
                session.ViewingItemId = testNumber; // Store which test is being viewed
            }
            else
            {
                await botClient.SendTextMessageAsync(chatId, "Извините, тест с таким номером не найден.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.ViewingTestList; // Take them back to the list view
            }
        }

        // This was for the OLD test system (UserTestState from Models/JSON)
        // static async Task DisplayCurrentQuestionAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        // {
        //     if (session.CurrentTestState == null || !session.CurrentTestState.IsTestActive) return;

        //     TestQuestion question = session.CurrentTestState.GetCurrentQuestion();
        //     if (question == null) { // Should not happen if IsTestActive is true
        //          await botClient.SendTextMessageAsync(chatId, "Error: Could not load the current question.", cancellationToken: ct);
        //         _userSessionService.EndUserTest(session.UserId); // End test to prevent loop
        //         return;
        //     }
        //     var questionText = $"Question {session.CurrentTestState.CurrentQuestionIndex + 1} of {session.CurrentTestState.CurrentTest.Questions.Count}:\n{question.QuestionText}\n\n";
        //     for (int i = 0; i < question.Options.Count; i++) {
        //         questionText += $"{i + 1}. {question.Options[i]}\n";
        //     }
        //     questionText += "\nYour answer (enter the number):";
        //     await botClient.SendTextMessageAsync(chatId, questionText, cancellationToken: ct);
        // }


        // New method for the new TestData structure
        static async Task DisplayCurrentTestQuestionAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (!session.ActiveTestId.HasValue || !activeTestsData.TryGetValue(session.ActiveTestId.Value, out var currentTestData))
            {
                await botClient.SendTextMessageAsync(chatId, "Ошибка: Тест не найден или не активен.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.EndCurrentTest(); // Reset test state
                session.CurrentState = UserCurrentState.MainMenu;
                return;
            }

            if (session.CurrentQuestionIndex >= currentTestData.Questions.Count)
            {
                // This case should ideally be handled by the answer processing logic before calling display.
                // However, as a safeguard:
                await botClient.SendTextMessageAsync(chatId, "Кажется, все вопросы закончились, но тест не был завершен корректно.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.EndCurrentTest();
                session.CurrentState = UserCurrentState.MainMenu;
                return;
            }

            QuestionData question = currentTestData.Questions[session.CurrentQuestionIndex];

            var keyboardButtons = question.Options.Select(option => new KeyboardButton(option)).ToArray();
            var replyKeyboardMarkup = new ReplyKeyboardMarkup(
                // Dynamically create rows for options, e.g., 2 options per row or 1 per row
                // For simplicity here, let's do one button per row for up to N buttons, then group.
                // A more sophisticated layout might be needed for many options.
                // For now, let's make each option its own row for clarity.
                keyboardButtons.Select(kb => new[] { kb })
            )
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true // Good for question-answer flow
            };

            string questionMessage = $"Вопрос {session.CurrentQuestionIndex + 1} из {currentTestData.Questions.Count}:\n\n{question.Text}";

            await botClient.SendTextMessageAsync(chatId, questionMessage, replyMarkup: replyKeyboardMarkup, cancellationToken: ct);
        }


        static async Task HandleStopTestCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            bool testWasStopped = false;
            if (session.ActiveTestId.HasValue && session.CurrentState == UserCurrentState.TakingTest)
            {
                var testName = activeTestsData.TryGetValue(session.ActiveTestId.Value, out var testData) ? testData.TestName : "текущий";
                session.EndCurrentTest(); // Clears new test state properties and sets state to MainMenu
                await botClient.SendTextMessageAsync(chatId, $"Тест \"{testName}\" остановлен. Ваш прогресс не сохранен.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                testWasStopped = true;
            }
            // Fallback for old test system state, if any, though it should be phased out.
            // This else-if might be removed if CurrentTestState is fully deprecated for active tests.
            else if (session.CurrentTestState != null && session.CurrentTestState.IsTestActive)
            {
                 _userSessionService.EndUserTest(session.UserId); // Ensure this is the method intended for the old system state.
                                                                // UserSession.EndUserTest might need review if it was also for the old system.
                                                                // For now, assuming it clears the old UserTestState.
                session.CurrentState = UserCurrentState.MainMenu; // Ensure state is reset
                await botClient.SendTextMessageAsync(chatId, "Тест (старая система) остановлен. Ваш прогресс не сохранен.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                testWasStopped = true;
            }

            if (!testWasStopped)
            {
                await botClient.SendTextMessageAsync(chatId, "Нет активного теста для остановки.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
            }
        }

        static async Task StartActualTestAsync(ITelegramBotClient botClient, UserSession session, long chatId, int testId, CancellationToken ct)
        {
            if (!activeTestsData.TryGetValue(testId, out var testToStart))
            {
                await botClient.SendTextMessageAsync(chatId, "Ошибка: Выбранный тест не найден.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.ViewingTestList; // Go back to test list context
                // Consider calling HandleTestsListAsync(botClient, session, chatId, ct); to re-display list
                return;
            }

            if (testToStart.Questions == null || !testToStart.Questions.Any())
            {
                await botClient.SendTextMessageAsync(chatId, $"Ошибка: В тесте \"{testToStart.TestName}\" нет вопросов.", replyMarkup: TestDetailKeyboard, cancellationToken: ct);
                // Keep them on TestDetailKeyboard or send to TestList? For now, TestDetail.
                session.CurrentState = UserCurrentState.ViewingTestDetail;
                return;
            }

            session.StartNewTest(testId); // Sets ActiveTestId, resets score/index, sets state to TakingTest

            // Send a confirmation message without a keyboard, as DisplayCurrentTestQuestionAsync will send the question keyboard.
            await botClient.SendTextMessageAsync(chatId, $"Начинаем тест: \"{testToStart.TestName}\"", cancellationToken: ct, replyMarkup: new ReplyKeyboardRemove());
            await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
        }

        static async Task HandleLessonsListAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentTestState != null && session.CurrentTestState.IsTestActive)
            {
                await botClient.SendTextMessageAsync(chatId, "Please finish or stop the current test (/stoptest) before viewing lessons.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                await DisplayCurrentQuestionAsync(botClient, session, chatId, ct);
                return;
            }

            if (availableLessons == null || !availableLessons.Any())
            {
                await botClient.SendTextMessageAsync(chatId, "Извините, список уроков пока пуст.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                return;
            }

            var messageBuilder = new System.Text.StringBuilder("Доступные уроки:\n");
            for (int i = 0; i < availableLessons.Count; i++)
            {
                messageBuilder.AppendLine($"{i + 1}. {availableLessons[i]}");
            }
            messageBuilder.AppendLine("\nВыберите урок, чтобы получить подробную информацию (функционал в следующем шаге).");

            await botClient.SendTextMessageAsync(chatId, messageBuilder.ToString(), replyMarkup: MainCommandKeyboard, cancellationToken: ct);
            session.CurrentState = UserCurrentState.ViewingLessonList;
        }

        static async Task HandleTestsListAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentTestState != null && session.CurrentTestState.IsTestActive)
            {
                await botClient.SendTextMessageAsync(chatId, "Please finish or stop the current test (/stoptest) before viewing the tests list.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                await DisplayCurrentQuestionAsync(botClient, session, chatId, ct);
                return;
            }

            if (availableTests == null || !availableTests.Any())
            {
                await botClient.SendTextMessageAsync(chatId, "Извините, список тестов пока пуст.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                return;
            }

            var messageBuilder = new System.Text.StringBuilder("Доступные тесты:\n");
            for (int i = 0; i < availableTests.Count; i++)
            {
                messageBuilder.AppendLine($"{i + 1}. {availableTests[i]}");
            }
            messageBuilder.AppendLine("\nВыберите тест для начала (реализация запуска тестов — позже).");

            await botClient.SendTextMessageAsync(chatId, messageBuilder.ToString(), replyMarkup: MainCommandKeyboard, cancellationToken: ct);
            session.CurrentState = UserCurrentState.ViewingTestList;
        }

        static async Task HandleLessonDetailAsync(ITelegramBotClient botClient, UserSession session, long chatId, int lessonNumber, CancellationToken ct)
        {
            if (lessonDetails.TryGetValue(lessonNumber, out string? detailText))
            {
                await SendLongMessageAsync(botClient, chatId, detailText, ct, LessonDetailKeyboard); // Use SendLongMessageAsync for potentially long details
                session.CurrentState = UserCurrentState.ViewingLessonDetail;
                session.ViewingItemId = lessonNumber; // Store which lesson is being viewed
            }
            else
            {
                await botClient.SendTextMessageAsync(chatId, "Извините, урок с таким номером не найден.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                // Optionally, revert state to ViewingLessonList or MainMenu if appropriate
                // For now, if they were in ViewingLessonList, they'd remain there implicitly until next valid action
                // Or explicitly set it back:
                session.CurrentState = UserCurrentState.ViewingLessonList; // Take them back to the list view contextually
                // Consider calling HandleLessonsListAsync here if you want to re-show the list immediately.
            }
        }

        // Helper to send potentially long messages by splitting them
        // Modified to accept an optional IReplyMarkup
        static async Task SendLongMessageAsync(ITelegramBotClient botClient, long chatId, string message, CancellationToken cancellationToken, IReplyMarkup? replyMarkup = null, int chunkSize = 4000)
        {
            if (string.IsNullOrEmpty(message)) return;
            var chunks = SplitMessage(message, chunkSize);
            for (int i = 0; i < chunks.Count; i++)
            {
                bool isLastChunk = i == chunks.Count - 1;
                await botClient.SendTextMessageAsync(
                    chatId,
                    chunks[i],
                    replyMarkup: isLastChunk ? replyMarkup : null, // Only send markup with the last chunk
                    cancellationToken: cancellationToken);

                if (!isLastChunk) // No delay after the last chunk
                {
                    await Task.Delay(200, cancellationToken); // Small delay to avoid rate limiting
                }
            }
        }

        // Helper to combine multiple short messages into one if possible, or send separately
        static async Task SendCombinedMessages(ITelegramBotClient botClient, long chatId, System.Collections.Generic.List<string> messages, CancellationToken cancellationToken, IReplyMarkup? replyMarkup = null)
        {
            string combined = string.Join("\n", messages);
            await SendLongMessageAsync(botClient, chatId, combined, cancellationToken, replyMarkup);
        }


        public static System.Collections.Generic.List<string> SplitMessage(string message, int chunkSize = 4000)
        {
            var messages = new System.Collections.Generic.List<string>();
            if (string.IsNullOrEmpty(message)) return messages;
            for (int i = 0; i < message.Length; i += chunkSize)
            {
                messages.Add(message.Substring(i, Math.Min(chunkSize, message.Length - i)));
            }
            return messages;
        }
    }

    // Module: Authorization (modified to use UserSessionService)
    public static class AuthorizationService
    {
        private const string HardcodedPassword = "omni_password123";

        public static bool Authenticate(long userId, string? password, UserSessionService sessionService)
        {
            if (password == HardcodedPassword)
            {
                sessionService.UpdateUserAuthentication(userId, true);
                Console.WriteLine($"Bot Response: Authentication successful for User {userId}. You now have access to all commands.");
                return true;
            }
            Console.WriteLine($"Bot Response: Authentication failed for User {userId}. Invalid password.");
            return false;
        }

        public static bool CheckAuthentication(long userId, UserSessionService sessionService) // No longer used directly by Program.cs commands
        {
            var session = sessionService.GetUserSession(userId);
            return session.IsAuthenticated;
        }

        public static void Logout(long userId, UserSessionService sessionService)
        {
            sessionService.UpdateUserAuthentication(userId, false);
            // Also clear any sensitive session data if needed, e.g., active test
            sessionService.EndUserTest(userId);
            Console.WriteLine($"Bot Response: User {userId} has been logged out.");
        }
    }

    // Module: Courses
    // Responsibilities:
    // - Loading course materials (Markdown, JSON)
    // - Providing lesson content
    // - Listing available courses and lessons
    public class CourseService
    {
        // TODO: Implement methods for fetching course/lesson data
    }

    // Module: Testing
    // Responsibilities:
    // - Loading test questions
    // - Conducting tests
    // - Grading tests and providing results
    public class TestingService
    {
        // TODO: Implement methods for test management
    }

    // Module: UserInteraction (Bot Logic)
    // Responsibilities:
    // - Handling Telegram API events (incoming messages, commands)
    // - Formatting and sending messages to users
    // - Coordinating actions between other modules
    public class UserInteractionService
    {
        // TODO: Main bot logic, command parsing, calling other services
    }
}
