using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
// Removed aliases, will use FQTNs or direct namespace usings
// using ExistingUserSessionService = Omnieye.Bot.Services.UserSessionService;
// using NewCourseContentLoaderService = OmnieyeBot.Services.CourseContentLoaderService;
using OmnieyeBot.Services; // For CourseContentLoaderService
using Omnieye.Bot.Services;  // For UserSessionService (existing)


// CommandRouter is in the same namespace, so direct reference is fine.

namespace OmnieyeBot.BotHandlers
{
    public class MessageHandler
    {
        private readonly ITelegramBotClient _botClient;
        private readonly Omnieye.Bot.Services.UserSessionService _userSessionService;
        private readonly OmnieyeBot.Services.CourseContentLoaderService _courseContentLoaderService;
        private readonly CommandRouter _commandRouter;

        public MessageHandler(ITelegramBotClient botClient,
                              Omnieye.Bot.Services.UserSessionService userSessionService,
                              OmnieyeBot.Services.CourseContentLoaderService courseContentLoaderService)
        {
            _botClient = botClient;
            _userSessionService = userSessionService;
            _courseContentLoaderService = courseContentLoaderService;

            // Pass botClient, userSessionService, and courseContentLoaderService to CommandRouter
            _commandRouter = new CommandRouter(_botClient, _userSessionService, _courseContentLoaderService);
        }

        public async Task<bool> HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            // The botClient parameter here is the one from StartReceiving, can be used directly
            // or use the one stored in _botClient. For consistency, let's use the instance member.

            if (update.Type != UpdateType.Message)
                return false; // Not a message, not handled by this logic
            if (update.Message!.Type != MessageType.Text)
                return false; // Not a text message, not handled by this logic

            var message = update.Message;
            var chatId = message.Chat.Id;
            var messageText = message.Text;

            if (string.IsNullOrEmpty(messageText))
                return false; // Empty message, not handled

            // Log can be here or inside CommandRouter if specific to course commands
            // Console.WriteLine($"CourseMessageHandler attempting to handle '{messageText}' in chat {chatId}.");
            Console.WriteLine($"[MessageHandler] Attempting to route message: '{messageText}' via CommandRouter."); // DEBUG LOG

            // Delegate to CommandRouter for processing and return its handling status
            bool handled = await _commandRouter.RouteAsync(message, cancellationToken);
            Console.WriteLine($"[MessageHandler] CommandRouter handled status: {handled} for message: '{messageText}'"); // DEBUG LOG
            return handled;
        }

        public async Task HandleCallbackQueryAsync(ITelegramBotClient botClient, CallbackQuery callbackQuery, CancellationToken cancellationToken)
        {
            if (callbackQuery.Message == null || string.IsNullOrEmpty(callbackQuery.Data))
            {
                Console.WriteLine("[MessageHandler] Received CallbackQuery with no Message or Data.");
                return;
            }

            long chatId = callbackQuery.Message.Chat.Id;
            int messageId = callbackQuery.Message.MessageId; // Message to potentially edit
            string callbackData = callbackQuery.Data;

            Console.WriteLine($"[MessageHandler] Received CallbackQuery: Data='{callbackData}', ChatId='{chatId}', MessageId='{messageId}'");

            var userSession = _userSessionService.GetUserSession(chatId); // ExistingUserSessionService

            // Answer callback query to remove the "loading" state on the button
            try
            {
                await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MessageHandler] Error answering callback query: {ex.Message}");
                // Non-critical, continue processing
            }

            // Route based on callbackData prefix
            if (callbackData.StartsWith("flashcards_"))
            {
                await _commandRouter.HandleStartFlashcardSessionCallbackAsync(callbackData, userSession, chatId, cancellationToken);
            }
            else if (callbackData.StartsWith("show_answer_"))
            {
                // Pass messageId for potential editing
                // This is where GetLastBotMessageId was problematic. We should use callbackQuery.Message.MessageId
                await _commandRouter.HandleShowAnswerCallbackAsync(callbackData, userSession, chatId, /* pass messageId for editing */ callbackQuery.Message.MessageId, cancellationToken);
            }
            else if (callbackData.StartsWith("next_flashcard_"))
            {
                await _commandRouter.HandleNextFlashcardCallbackAsync(callbackData, userSession, chatId, cancellationToken);
            }
            else if (callbackData.StartsWith("exit_flashcards_"))
            {
                await _commandRouter.HandleExitFlashcardsCallbackAsync(callbackData, userSession, chatId, cancellationToken);
            }
            else if (callbackData.StartsWith("quiz_")) // General prefix for quiz actions
            {
                if (callbackData.Contains("_answer_")) // E.g., quiz_answer_{moduleId}_{lessonId}_{qIndex}_{optIndex}
                {
                    await _commandRouter.HandleQuizAnswerCallbackAsync(callbackData, userSession, chatId, callbackQuery.Message.MessageId, cancellationToken);
                }
                else // E.g., quiz_{moduleId}_{lessonId} for starting quiz
                {
                    await _commandRouter.HandleStartQuizSessionCallbackAsync(callbackData, userSession, chatId, cancellationToken);
                }
            }
            else if (callbackData.StartsWith("exit_quiz_")) // Specific exit if added as a button during quiz
            {
                 await _commandRouter.HandleExitQuizCallbackAsync(callbackData, userSession, chatId, cancellationToken);
            }
            // Add more callback routes here as needed
            else
            {
                Console.WriteLine($"[MessageHandler] Unknown CallbackQuery Data: {callbackData}");
                // Optionally send a message to user or just ignore.
                // await botClient.SendTextMessageAsync(chatId, "Неизвестное действие.", cancellationToken: cancellationToken);
            }
        }
    }
}
