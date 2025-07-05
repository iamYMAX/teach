using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using OmnieyeBot.Services;
// No direct model usage here, models are used by services or CommandRouter
// No direct keyboard usage here, keyboards are used by CommandRouter

namespace OmnieyeBot.BotHandlers
{
    public class MessageHandler
    {
        private readonly ITelegramBotClient _botClient; // Passed to CommandRouter
        private readonly CourseService _courseService; // Passed to CommandRouter
        private readonly UserSessionService _userSessionService; // Passed to CommandRouter
        private readonly CommandRouter _commandRouter;

        public MessageHandler(ITelegramBotClient botClient, CourseService courseService, UserSessionService userSessionService)
        {
            _botClient = botClient;
            _courseService = courseService;
            _userSessionService = userSessionService;

            _commandRouter = new CommandRouter(botClient, courseService, userSessionService);
        }

        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Type != UpdateType.Message)
                return;
            if (update.Message!.Type != MessageType.Text)
                return;

            var message = update.Message;
            var chatId = message.Chat.Id;
            var messageText = message.Text;

            if (string.IsNullOrEmpty(messageText)) return;

            Console.WriteLine($"Received a '{messageText}' message in chat {chatId}.");

            await _commandRouter.RouteAsync(message, cancellationToken);
        }
    }
}
