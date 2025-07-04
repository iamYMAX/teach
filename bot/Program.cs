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

namespace Omnieye.Bot
{
    class Program
    {
        private static TestLoaderService _testLoaderService = new TestLoaderService();
        private static UserSessionService _userSessionService = new UserSessionService();
        private static MaterialLoader _materialLoader = new MaterialLoader(); // Initialize MaterialLoader

        private static ITelegramBotClient? _botClient;
        private static CancellationTokenSource? _cts;

        static async Task Main(string[] args)
        {
            Console.WriteLine("Omnieye Telegram Bot starting...");

            // IMPORTANT: Replace with your actual bot token from BotFather
            var botToken = Environment.GetEnvironmentVariable("OMNIEYE_BOT_TOKEN") ?? "7266317536:AAG-5KzhaNK5Q_SYrl-XHo5MyXJGpiAzXck";
            if (botToken == "7266317536:AAG-5KzhaNK5Q_SYrl-XHo5MyXJGpiAzXck")
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
            if (session.CurrentTestState != null && session.CurrentTestState.IsTestActive)
            {
                if (int.TryParse(messageText, out int answerOpt) && answerOpt > 0 &&
                    session.CurrentTestState.GetCurrentQuestion() != null && // Ensure question is loaded
                    answerOpt <= session.CurrentTestState.GetCurrentQuestion().Options.Count)
                {
                    await HandleAnswerInputAsync(botClient, userId, chatId, answerOpt - 1, cancellationToken);
                    return;
                }
                // If not a valid answer, it might be /stoptest or /help during a test
            }

            var parts = messageText.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLower();
            var argument = parts.Length > 1 ? parts[1] : null;

            if (!session.IsAuthenticated && command != "/login" && command != "/start" && command != "/help") // Allow /start & /help before login
            {
                await botClient.SendTextMessageAsync(chatId, "You are not authenticated. Please use /login <password> to authenticate.", cancellationToken: cancellationToken);
                return;
            }

            try
            {
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
                await botClient.SendTextMessageAsync(chatId, "Authentication successful. You now have access to all commands.", cancellationToken: ct);
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
            AuthorizationService.Logout(session.UserId, _userSessionService);
            await botClient.SendTextMessageAsync(chatId, "You have been logged out.", cancellationToken: ct);
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
            else
            {
                await SendCombinedMessages(botClient, chatId, messages, ct);
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

        static async Task DisplayCurrentQuestionAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentTestState == null || !session.CurrentTestState.IsTestActive) return;

            TestQuestion question = session.CurrentTestState.GetCurrentQuestion();
            if (question == null) { // Should not happen if IsTestActive is true
                 await botClient.SendTextMessageAsync(chatId, "Error: Could not load the current question.", cancellationToken: ct);
                _userSessionService.EndUserTest(session.UserId); // End test to prevent loop
                return;
            }
            var questionText = $"Question {session.CurrentTestState.CurrentQuestionIndex + 1} of {session.CurrentTestState.CurrentTest.Questions.Count}:\n{question.QuestionText}\n\n";
            for (int i = 0; i < question.Options.Count; i++) {
                questionText += $"{i + 1}. {question.Options[i]}\n";
            }
            questionText += "\nYour answer (enter the number):";
            await botClient.SendTextMessageAsync(chatId, questionText, cancellationToken: ct);
        }

        static async Task HandleStopTestCommandAsync(ITelegramBotClient botClient, UserSession session, long chatId, CancellationToken ct)
        {
            if (session.CurrentTestState != null && session.CurrentTestState.IsTestActive) {
                _userSessionService.EndUserTest(session.UserId);
                await botClient.SendTextMessageAsync(chatId, "Test stopped. Your progress for this test was not saved.", cancellationToken: ct);
            } else {
                await botClient.SendTextMessageAsync(chatId, "No active test to stop.", cancellationToken: ct);
            }
        }

        // Helper to send potentially long messages by splitting them
        static async Task SendLongMessageAsync(ITelegramBotClient botClient, long chatId, string message, CancellationToken cancellationToken, int chunkSize = 4000)
        {
            if (string.IsNullOrEmpty(message)) return;
            var chunks = SplitMessage(message, chunkSize);
            foreach (var chunk in chunks)
            {
                await botClient.SendTextMessageAsync(chatId, chunk, cancellationToken: cancellationToken);
                await Task.Delay(200, cancellationToken); // Small delay to avoid rate limiting, if necessary
            }
        }

        // Helper to combine multiple short messages into one if possible, or send separately
        static async Task SendCombinedMessages(ITelegramBotClient botClient, long chatId, System.Collections.Generic.List<string> messages, CancellationToken cancellationToken)
        {
            string combined = string.Join("\n", messages);
            await SendLongMessageAsync(botClient, chatId, combined, cancellationToken);
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
