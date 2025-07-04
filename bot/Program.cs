using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
// using Omnieye.Bot.Models; // No longer needed if TestLoaderService and old Models.Test are fully deprecated
using Omnieye.Bot.Services;
using Omnieye.Bot.States;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Omnieye.Bot
{
    class Program
    {
        // private static TestLoaderService _testLoaderService = new TestLoaderService(); // OLD SYSTEM - DEPRECATED
        private static UserSessionService _userSessionService = new UserSessionService();
        private static MaterialLoader _materialLoader = new MaterialLoader();

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
            { 1, "Тест 1: Проверка знаний по основам\n\nВключает 10 вопросов по базовым темам." }, // Note: question count is descriptive
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

        private static readonly ReplyKeyboardMarkup AfterTestMenuKeyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "Вернуться в меню" }
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };


        // --- Test Data Structures (In-Program definition) ---
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
            public string TestName { get; }
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

            var receiverOptions = new ReceiverOptions { AllowedUpdates = Array.Empty<UpdateType>() };
            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
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

        static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Message is not { } message) return;
            if (message.From is not { } user) return;
            if (message.Text is not { } messageText) return;

            long userId = user.Id;
            long chatId = message.Chat.Id;
            var session = _userSessionService.GetUserSession(userId);

            Console.WriteLine($"Received '{messageText}' from User {userId} in Chat {chatId}. State: {session.CurrentState}");

            // 1. Handle active test input (highest priority)
            if (session.CurrentState == UserCurrentState.TakingTest)
            {
                if (!session.ActiveTestId.HasValue || !activeTestsData.TryGetValue(session.ActiveTestId.Value, out var currentTestData) ||
                    session.CurrentQuestionIndex >= currentTestData.Questions.Count)
                {
                    await botClient.SendTextMessageAsync(chatId, "Произошла ошибка с текущим тестом. Возвращаемся в главное меню.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                    session.EndCurrentTest(); // Resets test vars and sets state to MainMenu
                    return;
                }

                QuestionData currentQuestion = currentTestData.Questions[session.CurrentQuestionIndex];
                int selectedOptionIdx = currentQuestion.Options.IndexOf(messageText);

                if (selectedOptionIdx != -1) // Answer matches an option
                {
                    if (selectedOptionIdx == currentQuestion.CorrectOptionIndex) session.CurrentTestScore++;
                    session.CurrentQuestionIndex++;

                    if (session.CurrentQuestionIndex < currentTestData.Questions.Count)
                    {
                        await DisplayCurrentTestQuestionAsync(botClient, session, chatId, cancellationToken);
                    }
                    else // Test finished
                    {
                        string resultMessage = $"Тест \"{currentTestData.TestName}\" завершён.\nВаш результат: {session.CurrentTestScore} из {currentTestData.Questions.Count}.";
                        await botClient.SendTextMessageAsync(chatId, resultMessage, replyMarkup: AfterTestMenuKeyboard, cancellationToken: cancellationToken);
                        session.EndCurrentTest(); // Resets test vars and sets state to MainMenu
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

            // 2. Handle keyboard button presses if authenticated
            if (session.IsAuthenticated)
            {
                bool keyboardButtonProcessed = true;
                switch (messageText)
                {
                    case "📘 Уроки": await HandleLessonsListAsync(botClient, session, chatId, cancellationToken); break;
                    case "🧪 Тесты": await HandleTestsListAsync(botClient, session, chatId, cancellationToken); break;
                    case "Назад":
                        if (session.CurrentState == UserCurrentState.ViewingLessonDetail) await HandleLessonsListAsync(botClient, session, chatId, cancellationToken);
                        else if (session.CurrentState == UserCurrentState.ViewingTestDetail) await HandleTestsListAsync(botClient, session, chatId, cancellationToken);
                        else // Fallback "Назад" if state is unexpected (e.g. MainMenu but "Назад" somehow pressed)
                        {
                            await botClient.SendTextMessageAsync(chatId, "Главное меню.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                            session.CurrentState = UserCurrentState.MainMenu;
                        }
                        break;
                    case "Начать тест":
                        if (session.CurrentState == UserCurrentState.ViewingTestDetail && session.ViewingItemId.HasValue) await StartActualTestAsync(botClient, session, chatId, session.ViewingItemId.Value, cancellationToken);
                        else if (session.CurrentState == UserCurrentState.ViewingTestDetail && !session.ViewingItemId.HasValue) await botClient.SendTextMessageAsync(chatId, "Ошибка: не удалось определить, какой тест запустить.", replyMarkup: TestDetailKeyboard, cancellationToken: cancellationToken);
                        else await botClient.SendTextMessageAsync(chatId, "Пожалуйста, сначала выберите тест из списка.", replyMarkup: MainCommandKeyboard, cancellationToken: cancellationToken);
                        break;
                    case "Вернуться в меню": // After test completion
                        await HandleStartCommandAsync(botClient, session, chatId, cancellationToken); // This will show main menu and keyboard
                        session.CurrentState = UserCurrentState.MainMenu; // Ensure state
                        break;
                    case "🔐 Выйти": await HandleLogoutCommandAsync(botClient, session, chatId, cancellationToken); break;
                    default: keyboardButtonProcessed = false; break;
                }
                if (keyboardButtonProcessed) return;
            }

            // 3. Handle numeric selection if authenticated and in a list view
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
                if (selectionHandled) return;
            }

            // 4. Process standard slash commands
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
                    case "/courses": await HandleCoursesCommandAsync(botClient, session, chatId, cancellationToken); break;
                    case "/lesson": await HandleLessonCommandAsync(botClient, session, chatId, argument, cancellationToken); break;
                    case "/test": await HandleTestCommandAsync(botClient, session, chatId, argument, cancellationToken); break;
                    case "/stoptest": await HandleStopTestCommandAsync(botClient, session, chatId, cancellationToken); break;
                    default:
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

        static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };
            Console.WriteLine(ErrorMessage);
            return Task.CompletedTask;
        }

        static async Task HandleLoginCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, string? password, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем пытаться войти.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }
            if (session.IsAuthenticated) {
                await botClient.SendTextMessageAsync(chatId, "You are already authenticated.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                return;
            }
            string? trimmedPassword = password?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedPassword)) {
                await botClient.SendTextMessageAsync(chatId, "Please provide a password. Usage: /login <password>", cancellationToken: ct);
                return;
            }
            if (AuthorizationService.Authenticate(session.UserId, trimmedPassword, _userSessionService)) {
                await botClient.SendTextMessageAsync(chatId, "Вы успешно авторизованы.\nВыберите действие:", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.MainMenu;
            } else {
                await botClient.SendTextMessageAsync(chatId, "Authentication failed. Invalid password.", cancellationToken: ct);
            }
        }

        static async Task HandleLogoutCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue)  {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем выходить из системы.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }
            if (!session.IsAuthenticated) {
                 await botClient.SendTextMessageAsync(chatId, "You are not currently authenticated.", cancellationToken: ct);
                return;
            }
            AuthorizationService.Logout(session.UserId, _userSessionService); // Clears auth and current test state via service
            session.CurrentState = UserCurrentState.MainMenu; // Ensure nav state is reset
            await botClient.SendTextMessageAsync(chatId, "You have been logged out.", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: ct);
        }

        static async Task HandleStartCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            var messages = new List<string> { "Welcome to Omnieye Certification Bot! Use /courses to see available courses, or /help for more commands." };
            if (!session.IsAuthenticated) messages.Add("Please use /login <password> to access content.");

            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue)
            {
                await botClient.SendTextMessageAsync(chatId, "Вы находитесь в процессе теста. Введите ответ или /stoptest для остановки.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
            }
            else if (session.IsAuthenticated)
            {
                await SendCombinedMessages(botClient, chatId, messages, ct, MainCommandKeyboard);
                session.CurrentState = UserCurrentState.MainMenu; // Ensure state
            }
            else
            {
                await SendCombinedMessages(botClient, chatId, messages, ct);
            }
        }

        static async Task HandleHelpCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            var messages = new List<string> { "Available commands:" };
            IReplyMarkup? currentKeyboard = null;

            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue)
            {
                messages.Add("Вы находитесь в процессе теста.");
                messages.Add("Выберите вариант ответа кнопкой или введите /stoptest для остановки теста.");
                messages.Add("/stoptest - Stop the current test.");
                // Keep current question keyboard
            }
            else if (!session.IsAuthenticated)
            {
                messages.Add("/login <password> - Authenticate to access the bot");
                messages.Add("/start - Welcome message");
            }
            else // Authenticated & not TakingTest
            {
                messages.Add("/logout - Log out from the bot");
                messages.Add("/courses - List available courses (or use '📘 Уроки' button)");
                messages.Add("/lesson <number> - Get lesson content");
                messages.Add("/test <number> - Start a specific test (or use '🧪 Тесты' button)");
                messages.Add("/start - Welcome message & main menu");

                if (session.CurrentState == UserCurrentState.ViewingLessonDetail) currentKeyboard = LessonDetailKeyboard;
                else if (session.CurrentState == UserCurrentState.ViewingTestDetail) currentKeyboard = TestDetailKeyboard;
                else currentKeyboard = MainCommandKeyboard;
            }
            messages.Add("/help - Show this help message");
            await SendCombinedMessages(botClient, chatId, messages, ct, currentKeyboard);
        }

        static async Task HandleCoursesCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем просматривать курсы.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }
            await HandleLessonsListAsync(botClient, session, chatId, ct);
        }

        static async Task HandleLessonCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, string? argument, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем просматривать урок.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }
            if (string.IsNullOrWhiteSpace(argument) || !int.TryParse(argument, out int lessonNumber) || lessonNumber <= 0) {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, укажите номер урока. Например: /lesson 1", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.MainMenu; // Or ViewingLessonList if they were there
                return;
            }
            await HandleLessonDetailAsync(botClient, session, chatId, lessonNumber, ct);
        }

        static async Task HandleTestCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, string? argument, CancellationToken ct)
        {
             if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue) {
                await botClient.SendTextMessageAsync(chatId, "Вы уже находитесь в процессе теста. Введите ответ или /stoptest для остановки.", cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }
            if (string.IsNullOrWhiteSpace(argument) || !int.TryParse(argument, out int testIdToStart) || testIdToStart <= 0)
            {
                await HandleTestsListAsync(botClient, session, chatId, ct);
                await botClient.SendTextMessageAsync(chatId, "Чтобы начать конкретный тест командой, введите /test <номер_теста>.", cancellationToken: ct);
                return;
            }
            await StartActualTestAsync(botClient, session, chatId, testIdToStart, ct);
        }

        static async Task HandleLessonsListAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue)
            {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем просматривать уроки.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }
            if (availableLessons == null || !availableLessons.Any()) {
                await botClient.SendTextMessageAsync(chatId, "Извините, список уроков пока пуст.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                return;
            }
            var messageBuilder = new StringBuilder("Доступные уроки:\n");
            for (int i = 0; i < availableLessons.Count; i++) messageBuilder.AppendLine($"{i + 1}. {availableLessons[i]}");
            messageBuilder.AppendLine("\nОтправьте номер урока для просмотра деталей.");
            await botClient.SendTextMessageAsync(chatId, messageBuilder.ToString(), replyMarkup: MainCommandKeyboard, cancellationToken: ct);
            session.CurrentState = UserCurrentState.ViewingLessonList;
        }

        static async Task HandleTestsListAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentState == UserCurrentState.TakingTest && session.ActiveTestId.HasValue)
            {
                await botClient.SendTextMessageAsync(chatId, "Пожалуйста, завершите или остановите текущий тест (команда /stoptest), прежде чем просматривать список тестов.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
                return;
            }
            if (availableTests == null || !availableTests.Any()) {
                await botClient.SendTextMessageAsync(chatId, "Извините, список тестов пока пуст.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                return;
            }
            var messageBuilder = new StringBuilder("Доступные тесты:\n");
            for (int i = 0; i < availableTests.Count; i++) messageBuilder.AppendLine($"{i + 1}. {availableTests[i]}");
            messageBuilder.AppendLine("\nОтправьте номер теста для просмотра информации и начала.");
            await botClient.SendTextMessageAsync(chatId, messageBuilder.ToString(), replyMarkup: MainCommandKeyboard, cancellationToken: ct);
            session.CurrentState = UserCurrentState.ViewingTestList;
        }

        static async Task HandleLessonDetailAsync(ITelegramBotClient botClient, UserSession session, long chatId, int lessonNumber, CancellationToken ct)
        {
            if (lessonDetails.TryGetValue(lessonNumber, out string? detailText))
            {
                await SendLongMessageAsync(botClient, chatId, detailText, ct, LessonDetailKeyboard);
                session.CurrentState = UserCurrentState.ViewingLessonDetail;
                session.ViewingItemId = lessonNumber;
            }
            else
            {
                await botClient.SendTextMessageAsync(chatId, "Извините, урок с таким номером не найден.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.ViewingLessonList;
            }
        }

        static async Task HandleTestDetailAsync(ITelegramBotClient botClient, UserSession session, long chatId, int testNumber, CancellationToken ct)
        {
            if (testDetails.TryGetValue(testNumber, out string? detailText))
            {
                await SendLongMessageAsync(botClient, chatId, detailText, ct, TestDetailKeyboard);
                session.CurrentState = UserCurrentState.ViewingTestDetail;
                session.ViewingItemId = testNumber;
            }
            else
            {
                await botClient.SendTextMessageAsync(chatId, "Извините, тест с таким номером не найден.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.ViewingTestList;
            }
        }

        static async Task StartActualTestAsync(ITelegramBotClient botClient, UserSession session, long chatId, int testId, CancellationToken ct)
        {
            if (!activeTestsData.TryGetValue(testId, out var testToStart))
            {
                await botClient.SendTextMessageAsync(chatId, "Ошибка: Выбранный тест не найден.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.ViewingTestList;
                return;
            }
            if (testToStart.Questions == null || !testToStart.Questions.Any())
            {
                await botClient.SendTextMessageAsync(chatId, $"Ошибка: В тесте \"{testToStart.TestName}\" нет вопросов.", replyMarkup: TestDetailKeyboard, cancellationToken: ct);
                session.CurrentState = UserCurrentState.ViewingTestDetail;
                return;
            }
            session.StartNewTest(testId);
            await botClient.SendTextMessageAsync(chatId, $"Начинаем тест: \"{testToStart.TestName}\"", cancellationToken: ct, replyMarkup: new ReplyKeyboardRemove());
            await DisplayCurrentTestQuestionAsync(botClient, session, chatId, ct);
        }

        static async Task DisplayCurrentTestQuestionAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (!session.ActiveTestId.HasValue || !activeTestsData.TryGetValue(session.ActiveTestId.Value, out var currentTestData))
            {
                await botClient.SendTextMessageAsync(chatId, "Ошибка: Тест не найден или не активен.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.EndCurrentTest();
                session.CurrentState = UserCurrentState.MainMenu;
                return;
            }
            if (session.CurrentQuestionIndex >= currentTestData.Questions.Count)
            {
                await botClient.SendTextMessageAsync(chatId, "Кажется, все вопросы закончились, но тест не был завершен корректно.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                session.EndCurrentTest();
                session.CurrentState = UserCurrentState.MainMenu;
                return;
            }
            QuestionData question = currentTestData.Questions[session.CurrentQuestionIndex];
            var keyboardButtons = question.Options.Select(option => new KeyboardButton(option)).ToArray();
            var replyKeyboardMarkup = new ReplyKeyboardMarkup(keyboardButtons.Select(kb => new[] { kb }))
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
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
                session.EndCurrentTest(); // Resets test variables and sets state to MainMenu
                await botClient.SendTextMessageAsync(chatId, $"Тест \"{testName}\" остановлен. Ваш прогресс не сохранен.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                testWasStopped = true;
            }
            // Fallback for old test system state - can be removed if CurrentTestState is fully deprecated
            else if (session.CurrentTestState != null && session.CurrentTestState.IsTestActive)
            {
                _userSessionService.EndUserTest(session.UserId);
                session.CurrentState = UserCurrentState.MainMenu;
                await botClient.SendTextMessageAsync(chatId, "Тест (старая система) остановлен. Ваш прогресс не сохранен.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
                testWasStopped = true;
            }
            if (!testWasStopped)
            {
                await botClient.SendTextMessageAsync(chatId, "Нет активного теста для остановки.", replyMarkup: MainCommandKeyboard, cancellationToken: ct);
            }
        }

        static async Task SendLongMessageAsync(ITelegramBotClient botClient, long chatId, string message, CancellationToken cancellationToken, IReplyMarkup? replyMarkup = null, int chunkSize = 4000)
        {
            if (string.IsNullOrEmpty(message)) return;
            var chunks = SplitMessage(message, chunkSize);
            for (int i = 0; i < chunks.Count; i++)
            {
                bool isLastChunk = i == chunks.Count - 1;
                await botClient.SendTextMessageAsync(chatId, chunks[i], replyMarkup: isLastChunk ? replyMarkup : null, cancellationToken: cancellationToken);
                if (!isLastChunk) await Task.Delay(200, cancellationToken);
            }
        }

        static async Task SendCombinedMessages(ITelegramBotClient botClient, long chatId, List<string> messages, CancellationToken cancellationToken, IReplyMarkup? replyMarkup = null)
        {
            string combined = string.Join("\n", messages);
            await SendLongMessageAsync(botClient, chatId, combined, cancellationToken, replyMarkup);
        }

        public static List<string> SplitMessage(string message, int chunkSize = 4000)
        {
            var messages = new List<string>();
            if (string.IsNullOrEmpty(message)) return messages;
            for (int i = 0; i < message.Length; i += chunkSize)
            {
                messages.Add(message.Substring(i, Math.Min(chunkSize, message.Length - i)));
            }
            return messages;
        }
    } // End of Program class

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

        public static bool CheckAuthentication(long userId, UserSessionService sessionService)
        {
            var session = sessionService.GetUserSession(userId);
            return session.IsAuthenticated;
        }

        public static void Logout(long userId, UserSessionService sessionService)
        {
            sessionService.UpdateUserAuthentication(userId, false);
            var session = sessionService.GetUserSession(userId); // Get session to call EndCurrentTest
            session.EndCurrentTest(); // Ensure new test state is cleared
            // sessionService.EndUserTest(userId); // This was the old call, ensure it's correctly updated or EndCurrentTest is sufficient
            Console.WriteLine($"Bot Response: User {userId} has been logged out.");
        }
    }
}
