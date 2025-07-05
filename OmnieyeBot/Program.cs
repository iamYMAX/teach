using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
// It's good practice to use specific namespaces for your project classes
using OmnieyeBot.BotHandlers;
using OmnieyeBot.Services;
// Models will be used by services and handlers, so direct using here might not be needed
// using OmnieyeBot.Models;

namespace OmnieyeBot
{
    public class Program
    {
        // Made botClient public static so it can be accessed by handlers if needed,
        // though ideally it's passed via constructor or method parameters.
        // For now, CommandRouter will need access to it.
        public static ITelegramBotClient BotClient { get; private set; }

        // Services will be instantiated here and passed to handlers/routers
        private static CourseService _courseService;
        private static UserSessionService _userSessionService;
        // private static FlashcardService _flashcardService; // Example for future
        // ... other services

        public static async Task Main(string[] args)
        {
            // Replace "YOUR_BOT_TOKEN" with your actual bot token from environment variables or a config file
            var botToken = Environment.GetEnvironmentVariable("OMNIEYE_BOT_TOKEN") ?? "YOUR_BOT_TOKEN";
            if (botToken == "YOUR_BOT_TOKEN")
            {
                Console.WriteLine("Warning: Bot token is not set. Please set OMNIEYE_BOT_TOKEN environment variable.");
                // return; // Or allow to continue if testing without a real token is intended
            }

            BotClient = new TelegramBotClient(botToken);

            // Initialize services
            _courseService = new CourseService();
            _userSessionService = new UserSessionService();
            // _flashcardService = new FlashcardService(); // etc.

            // Initialize handlers, passing necessary dependencies
            // MessageHandler will internally create or use CommandRouter
            var messageHandler = new MessageHandler(BotClient, _courseService, _userSessionService);

            using var cts = new CancellationTokenSource();

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>() // receive all update types
            };

            BotClient.StartReceiving(
                updateHandler: messageHandler.HandleUpdateAsync, // Delegate to MessageHandler
                pollingErrorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: cts.Token
            );

            var me = await BotClient.GetMeAsync();
            Console.WriteLine($"Start listening for @{me.Username}");
            Console.ReadLine(); // Keep console open

            cts.Cancel();
        }

        private static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var errorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine(errorMessage);
            // In a production bot, you might want to log this to a file or a logging service
            return Task.CompletedTask;
        }
    }
}
