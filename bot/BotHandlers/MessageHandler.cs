using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
// Explicitly using both service namespaces or aliases
using NewCourseService = OmnieyeBot.Services.CourseService;
using ExistingUserSessionService = Omnieye.Bot.Services.UserSessionService;

// CommandRouter is in the same namespace, so direct reference is fine.

namespace OmnieyeBot.BotHandlers
{
    public class MessageHandler
    {
        private readonly ITelegramBotClient _botClient;
        private readonly NewCourseService _courseService;
        private readonly ExistingUserSessionService _userSessionService;
        private readonly CommandRouter _commandRouter;

        public MessageHandler(ITelegramBotClient botClient, NewCourseService courseService, ExistingUserSessionService userSessionService)
        {
            _botClient = botClient;
            _courseService = courseService;
            _userSessionService = userSessionService;

            // Pass botClient, new courseService, and existing userSessionService to CommandRouter
            _commandRouter = new CommandRouter(_botClient, _courseService, _userSessionService);
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
    }
}
