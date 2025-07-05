using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using OmnieyeBot.Services;
// CommandRouter is in the same namespace, so direct reference is fine.
// using OmnieyeBot.BotHandlers; // Not strictly needed if in same namespace.

namespace OmnieyeBot.BotHandlers
{
    public class MessageHandler
    {
        private readonly ITelegramBotClient _botClient;
        private readonly CourseService _courseService;
        private readonly UserSessionService _userSessionService;
        private readonly CommandRouter _commandRouter;

        public MessageHandler(ITelegramBotClient botClient, CourseService courseService, UserSessionService userSessionService)
        {
            _botClient = botClient; // Will be passed to CommandRouter
            _courseService = courseService;
            _userSessionService = userSessionService;

            // Pass botClient to CommandRouter as it's needed for sending messages
            _commandRouter = new CommandRouter(botClient, _courseService, _userSessionService);
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

            // Delegate to CommandRouter for processing and return its handling status
            return await _commandRouter.RouteAsync(message, cancellationToken);
        }
    }
}
