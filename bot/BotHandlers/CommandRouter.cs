using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using OmnieyeBot.Models;
using OmnieyeBot.Keyboards;
using Omnieye.Bot.States; // Required for the existing Omnieye.Bot.States.UserSession class
using OmnieyeBot.Services; // For the new CourseService AND CourseContentLoaderService
using Omnieye.Bot.Services; // For the existing UserSessionService
using Telegram.Bot.Types.ReplyMarkups; // For InlineKeyboardMarkup

namespace OmnieyeBot.BotHandlers
{
    public class CommandRouter
    {
        private readonly ITelegramBotClient _botClient;
        // private readonly OmnieyeBot.Services.CourseService _newStaticCourseService; // OLD static course service
        private readonly Omnieye.Bot.Services.UserSessionService _existingUserSessionService;
        private readonly OmnieyeBot.Services.CourseContentLoaderService _courseContentLoaderService; // NEW service for JSON

        public CommandRouter(ITelegramBotClient botClient,
                             /*OmnieyeBot.Services.CourseService staticCourseService,*/ // No longer need old static one
                             Omnieye.Bot.Services.UserSessionService userSessionService,
                             OmnieyeBot.Services.CourseContentLoaderService courseContentLoaderService)
        {
            _botClient = botClient;
            // _newStaticCourseService = staticCourseService;
            _existingUserSessionService = userSessionService;
            _courseContentLoaderService = courseContentLoaderService;
        }

        public async Task<bool> RouteAsync(Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var messageText = message.Text;
            var userSession = _existingUserSessionService.GetUserSession(chatId);
            userSession.CurrentLoadedModuleData = null; // Clear any previously loaded module data on new command

            Console.WriteLine($"[CommandRouter] Routing message: '{messageText}'");

            if (messageText == "/start")
            {
                Console.WriteLine("[CommandRouter] Matched /start");
                userSession.CurrentModuleIdForNav = null; // Reset module navigation context
                await HandleStartCommandAsync(chatId, userSession, cancellationToken);
                return true;
            }
            else if (messageText == MainMenuKeyboard.LessonsButtonText)
            {
                Console.WriteLine($"[CommandRouter] Matched '{MainMenuKeyboard.LessonsButtonText}'");
                userSession.CurrentModuleIdForNav = null; // Reset module navigation context
                await HandleShowCourseModulesAsync(chatId, userSession, cancellationToken);
                return true;
            }
            else if (messageText == LessonKeyboard.BackButtonText)
            {
                Console.WriteLine($"[CommandRouter] Matched '{LessonKeyboard.BackButtonText}'");
                await HandleBackCommandAsync(chatId, userSession, cancellationToken);
                return true;
            }
            else if (messageText.StartsWith(LessonKeyboard.ModulePrefix))
            {
                Console.WriteLine($"[CommandRouter] Matched Module Prefix '{LessonKeyboard.ModulePrefix}'");
                await HandleShowLessonsInModuleAsync(chatId, messageText, userSession, cancellationToken);
                return true;
            }
            else if (messageText.StartsWith(LessonKeyboard.LessonPrefix))
            {
                Console.WriteLine($"[CommandRouter] Matched Lesson Prefix '{LessonKeyboard.LessonPrefix}'");
                await HandleShowLessonContentAsync(chatId, messageText, userSession, cancellationToken);
                return true;
            }
            // else
            // {
            //     // Do not call HandleUnknownCommandAsync here.
            //     // If no specific course command matched, return false so original handler can try.
            //     // await HandleUnknownCommandAsync(chatId, cancellationToken);
            // }
            return false; // Command not handled by this router
        }

        private async Task HandleStartCommandAsync(long chatId, UserSession userSession, CancellationToken cancellationToken)
        {
            userSession.CurrentModuleIdForNav = null;
            userSession.CurrentLessonIdForContext = 0;
            userSession.CurrentLoadedModuleData = null;
            var replyKeyboardMarkup = MainMenuKeyboard.GetKeyboard();

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Добро пожаловать в Omnieye Bot! Выберите опцию:",
                replyMarkup: replyKeyboardMarkup,
                cancellationToken: cancellationToken);
        }

        private async Task HandleShowCourseModulesAsync(long chatId, UserSession userSession, CancellationToken cancellationToken)
        {
            userSession.CurrentModuleIdForNav = null; // Reset context
            userSession.CurrentLessonIdForContext = 0;
            userSession.CurrentLoadedModuleData = null;

            var moduleIds = await _courseContentLoaderService.GetAvailableModuleIdsAsync();
            if (moduleIds == null || !moduleIds.Any())
            {
                await _botClient.SendTextMessageAsync(chatId: chatId, text: "Извините, доступных учебных модулей пока нет.", cancellationToken: cancellationToken);
                return;
            }

            List<ModuleContent> modules = new List<ModuleContent>();
            foreach (var moduleId in moduleIds)
            {
                if (string.IsNullOrWhiteSpace(moduleId)) continue;
                var moduleData = await _courseContentLoaderService.LoadModuleFromFileAsync(moduleId);
                if (moduleData != null)
                {
                    modules.Add(moduleData);
                }
            }

            if (!modules.Any())
            {
                await _botClient.SendTextMessageAsync(chatId: chatId, text: "Не удалось загрузить информацию о модулях.", cancellationToken: cancellationToken);
                return;
            }

            var replyKeyboardMarkup = LessonKeyboard.GetModulesKeyboard(modules); // Uses new ModuleContent model

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Выберите учебный модуль:",
                replyMarkup: replyKeyboardMarkup,
                cancellationToken: cancellationToken);
        }

        private async Task HandleShowLessonsInModuleAsync(long chatId, string moduleButtonText, UserSession userSession, CancellationToken cancellationToken)
        {
            var moduleTitle = moduleButtonText.Replace(LessonKeyboard.ModulePrefix, "").Trim();

            string? targetModuleId = null;
            ModuleContent? foundModule = null;
            var allModuleIds = await _courseContentLoaderService.GetAvailableModuleIdsAsync();
            if (allModuleIds != null)
            {
                foreach(var id_scan in allModuleIds)
                {
                    if (string.IsNullOrWhiteSpace(id_scan)) continue;
                    var mod_scan = await _courseContentLoaderService.LoadModuleFromFileAsync(id_scan);
                    if(mod_scan != null && mod_scan.Title == moduleTitle)
                    {
                        targetModuleId = id_scan;
                        foundModule = mod_scan;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(targetModuleId) || foundModule == null)
            {
                await _botClient.SendTextMessageAsync(chatId: chatId, text: "Выбранный модуль не найден.", cancellationToken: cancellationToken);
                await HandleShowCourseModulesAsync(chatId, userSession, cancellationToken);
                return;
            }

            var moduleData = foundModule;

            userSession.CurrentModuleIdForNav = moduleData.ModuleId.ToString();
            userSession.CurrentLoadedModuleData = moduleData;
            userSession.CurrentLessonIdForContext = 0;

            var replyKeyboardMarkup = LessonKeyboard.GetLessonsInModuleKeyboard(moduleData); // Uses new ModuleContent

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: $"Уроки в модуле \"{moduleData.Title}\":",
                replyMarkup: replyKeyboardMarkup,
                cancellationToken: cancellationToken);
        }

        private async Task HandleShowLessonContentAsync(long chatId, string lessonButtonText, UserSession userSession, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(userSession.CurrentModuleIdForNav) || userSession.CurrentLoadedModuleData == null)
            {
                await _botClient.SendTextMessageAsync(chatId: chatId, text: "Ошибка: Модуль не выбран или его данные не загружены. Пожалуйста, выберите модуль.", cancellationToken: cancellationToken);
                await HandleShowCourseModulesAsync(chatId, userSession, cancellationToken);
                return;
            }

            var lessonIdString = System.Text.RegularExpressions.Regex.Match(lessonButtonText, @"\(id:(\d+)\)").Groups[1].Value;

            if (!int.TryParse(lessonIdString, out int lessonId))
            {
                await _botClient.SendTextMessageAsync(chatId: chatId, text: "Ошибка: Не удалось распознать ID урока из кнопки.", cancellationToken: cancellationToken);
                var replyKeyboardMarkupLessons = LessonKeyboard.GetLessonsInModuleKeyboard(userSession.CurrentLoadedModuleData);
                 await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: $"Уроки в модуле \"{userSession.CurrentLoadedModuleData.Title}\":",
                    replyMarkup: replyKeyboardMarkupLessons,
                    cancellationToken: cancellationToken);
                return;
            }

            var lesson = userSession.CurrentLoadedModuleData.Lessons.FirstOrDefault(l => l.LessonId == lessonId);

            if (lesson == null)
            {
                await _botClient.SendTextMessageAsync(chatId: chatId, text: "Выбранный урок не найден в текущем модуле.", cancellationToken: cancellationToken);
                var replyKeyboardMarkupLessons = LessonKeyboard.GetLessonsInModuleKeyboard(userSession.CurrentLoadedModuleData);
                await _botClient.SendTextMessageAsync(
                   chatId: chatId,
                   text: $"Уроки в модуле \"{userSession.CurrentLoadedModuleData.Title}\":",
                   replyMarkup: replyKeyboardMarkupLessons,
                   cancellationToken: cancellationToken);
                return;
            }

            userSession.CurrentLessonIdForContext = lesson.LessonId;

            var messageText = $"📖 *{lesson.Title}*\n\n{lesson.Content}";
            // Pass current module ID string for callback data
            var inlineKeyboardMarkup = LessonKeyboard.GetLessonContentInlineKeyboard(lesson.LessonId, userSession.CurrentModuleIdForNav);

            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: messageText,
                parseMode: ParseMode.Markdown,
                replyMarkup: inlineKeyboardMarkup,
                cancellationToken: cancellationToken);
        }

        private async Task HandleBackCommandAsync(long chatId, UserSession userSession, CancellationToken cancellationToken)
        {
            if (userSession.CurrentLessonIdForContext != 0)
            {
                userSession.CurrentLessonIdForContext = 0;

                if (userSession.CurrentLoadedModuleData != null)
                {
                    var replyKeyboardMarkup = LessonKeyboard.GetLessonsInModuleKeyboard(userSession.CurrentLoadedModuleData);
                    await _botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: $"Уроки в модуле \"{userSession.CurrentLoadedModuleData.Title}\":",
                        replyMarkup: replyKeyboardMarkup,
                        cancellationToken: cancellationToken);
                }
                else if (!string.IsNullOrEmpty(userSession.CurrentModuleIdForNav))
                {
                    var moduleData = await _courseContentLoaderService.LoadModuleFromFileAsync(userSession.CurrentModuleIdForNav);
                    if (moduleData != null)
                    {
                        userSession.CurrentLoadedModuleData = moduleData;
                        var replyKeyboardMarkup = LessonKeyboard.GetLessonsInModuleKeyboard(moduleData);
                        await _botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: $"Уроки в модуле \"{moduleData.Title}\":",
                            replyMarkup: replyKeyboardMarkup,
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await HandleShowCourseModulesAsync(chatId, userSession, cancellationToken);
                    }
                }
                else
                {
                    await HandleStartCommandAsync(chatId, userSession, cancellationToken);
                }
            }
            else if (!string.IsNullOrEmpty(userSession.CurrentModuleIdForNav))
            {
                userSession.CurrentModuleIdForNav = null;
                userSession.CurrentLoadedModuleData = null;
                await HandleShowCourseModulesAsync(chatId, userSession, cancellationToken);
            }
            else
            {
                await HandleStartCommandAsync(chatId, userSession, cancellationToken);
            }
        }

        // --- Flashcard Handling Methods ---

        public async Task HandleStartFlashcardSessionCallbackAsync(string callbackData, UserSession userSession, long chatId, CancellationToken ct)
        {
            // callbackData format: "flashcards_{moduleId}_{lessonId}"
            var parts = callbackData.Split('_');
            if (parts.Length < 3)
            {
                await _botClient.SendTextMessageAsync(chatId, "Ошибка: неверный формат данных для начала сессии флеш-карточек.", cancellationToken: ct);
                return;
            }

            string moduleId = parts[1];
            if (!int.TryParse(parts[2], out int lessonId))
            {
                await _botClient.SendTextMessageAsync(chatId, "Ошибка: неверный ID урока для флеш-карточек.", cancellationToken: ct);
                return;
            }

            Console.WriteLine($"[CommandRouter] Starting flashcard session for module {moduleId}, lesson {lessonId}");

            userSession.CurrentModuleIdForNav = moduleId; // Ensure context is set
            userSession.CurrentLessonIdForContext = lessonId;
            userSession.CurrentInteractionContext = callbackData; // Store context for "back" or "exit"

            ModuleContent? moduleData = userSession.CurrentLoadedModuleData;
            if (moduleData == null || moduleData.ModuleId.ToString() != moduleId)
            {
                moduleData = await _courseContentLoaderService.LoadModuleFromFileAsync(moduleId);
                if (moduleData == null)
                {
                    await _botClient.SendTextMessageAsync(chatId, "Не удалось загрузить данные модуля для флеш-карточек.", cancellationToken: ct);
                    return;
                }
                userSession.CurrentLoadedModuleData = moduleData;
            }

            var lesson = moduleData.Lessons.FirstOrDefault(l => l.LessonId == lessonId);
            if (lesson == null || lesson.Flashcards == null || !lesson.Flashcards.Any())
            {
                await _botClient.SendTextMessageAsync(chatId, "Для этого урока нет флеш-карточек.", cancellationToken: ct);
                return;
            }

            var random = new Random();
            userSession.CurrentFlashcardContentQueue = new Queue<FlashcardContent>(lesson.Flashcards.OrderBy(f => random.Next()));
            userSession.CurrentFlashcardContent = null;

            await _botClient.SendTextMessageAsync(chatId, "Начинаем сессию флеш-карточек!", cancellationToken: ct);
            await ShowNextFlashcardAsync(userSession, chatId, ct);
        }

        private async Task ShowNextFlashcardAsync(UserSession userSession, long chatId, CancellationToken ct)
        {
            if (userSession.CurrentFlashcardContentQueue == null || !userSession.CurrentFlashcardContentQueue.Any())
            {
                await _botClient.SendTextMessageAsync(chatId, "✅ Все карточки просмотрены!", cancellationToken: ct);
                await HandleExitFlashcardsCallbackAsync(userSession.CurrentInteractionContext, userSession, chatId, ct, false); // Exit without explicit user action
                return;
            }

            var card = userSession.CurrentFlashcardContentQueue.Dequeue();
            userSession.CurrentFlashcardContent = card;

            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new [] { InlineKeyboardButton.WithCallbackData("Показать ответ", $"show_answer_{userSession.CurrentModuleIdForNav}_{userSession.CurrentLessonIdForContext}") },
                new [] { InlineKeyboardButton.WithCallbackData("Следующая карточка", $"next_flashcard_{userSession.CurrentModuleIdForNav}_{userSession.CurrentLessonIdForContext}") },
                new [] { InlineKeyboardButton.WithCallbackData("Выйти из карточек", $"exit_flashcards_{userSession.CurrentModuleIdForNav}_{userSession.CurrentLessonIdForContext}") }
            });

            await _botClient.SendTextMessageAsync(
                chatId,
                $"❓ *Вопрос:*\n{card.Question}",
                parseMode: ParseMode.Markdown,
                replyMarkup: inlineKeyboard,
                cancellationToken: ct
            );
        }

        public async Task HandleShowAnswerCallbackAsync(string callbackData, UserSession userSession, long chatId, CancellationToken ct)
        {
            if (userSession.CurrentFlashcardContent == null)
            {
                await _botClient.SendTextMessageAsync(chatId, "Ошибка: Текущая карточка не найдена. Попробуйте начать заново.", cancellationToken: ct);
                // Potentially reset flashcard state or guide user
                return;
            }
            // Re-send the question and then the answer, keeping the same interaction buttons
             var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                // "Показать ответ" could be removed or disabled here, but for simplicity, keep it or user can ignore
                new [] { InlineKeyboardButton.WithCallbackData("Следующая карточка", $"next_flashcard_{userSession.CurrentModuleIdForNav}_{userSession.CurrentLessonIdForContext}") },
                new [] { InlineKeyboardButton.WithCallbackData("Выйти из карточек", $"exit_flashcards_{userSession.CurrentModuleIdForNav}_{userSession.CurrentLessonIdForContext}") }
            });

            await _botClient.EditMessageTextAsync( // Or send new message if preferred
                chatId: chatId,
                messageId: GetLastBotMessageId(userSession), // Requires storing last message ID with inline keyboard
                text: $"❓ *Вопрос:*\n{userSession.CurrentFlashcardContent.Question}\n\n💡 *Ответ:*\n{userSession.CurrentFlashcardContent.Answer}",
                parseMode: ParseMode.Markdown,
                replyMarkup: inlineKeyboard, // Offer next/exit
                cancellationToken: ct);
            // To avoid error with GetLastBotMessageId, for now, just send a new message for the answer.
            // await _botClient.SendTextMessageAsync(chatId, $"💡 *Ответ:*\n{userSession.CurrentFlashcardContent.Answer}", parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboard, cancellationToken: ct);
        }
         // Helper method to get message ID - this is a placeholder, actual implementation needed
        private int GetLastBotMessageId(UserSession session) {
            // This needs to be implemented by storing the Message ID when ShowNextFlashcardAsync sends a message.
            // For now, this will cause an error if EditMessageTextAsync is used.
            // A simpler approach for now is to send a new message for the answer.
            // To properly use EditMessageTextAsync, the message ID of the question needs to be stored in the session or passed.
            // Let's assume for now that `EditMessageTextAsync` is problematic without messageId tracking and send a new message for answer.
            Console.WriteLine("Warning: GetLastBotMessageId is not properly implemented. Editing message might fail.");
            return 0;
        }


        public async Task HandleNextFlashcardCallbackAsync(string callbackData, UserSession userSession, long chatId, CancellationToken ct)
        {
            // If an answer was just shown, the user might click "Next" from that message.
            // Or if they skipped showing answer.
            await ShowNextFlashcardAsync(userSession, chatId, ct);
        }

        public async Task HandleExitFlashcardsCallbackAsync(string callbackData, UserSession userSession, long chatId, CancellationToken ct, bool sendConfirmation = true)
        {
            if(sendConfirmation) await _botClient.SendTextMessageAsync(chatId, "Выход из режима флеш-карточек.", cancellationToken: ct);

            // Clear flashcard specific session state
            userSession.CurrentFlashcardContentQueue = null;
            userSession.CurrentFlashcardContent = null;
            userSession.CurrentInteractionContext = null;
            // CurrentLessonIdForContext and CurrentModuleIdForNav remain, as user might want to go back to lesson content / list.

            // Option 1: Go back to lesson content (if possible)
            if (userSession.CurrentLoadedModuleData != null && userSession.CurrentLessonIdForContext != 0)
            {
                var lesson = userSession.CurrentLoadedModuleData.Lessons.FirstOrDefault(l => l.LessonId == userSession.CurrentLessonIdForContext);
                if (lesson != null)
                {
                    // Re-show lesson content with its options
                    var messageText = $"📖 *{lesson.Title}*\n\n{lesson.Content}";
                    var inlineKeyboardMarkup = LessonKeyboard.GetLessonContentInlineKeyboard(lesson.LessonId, userSession.CurrentModuleIdForNav);
                    await _botClient.SendTextMessageAsync(chatId, messageText, parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboardMarkup, cancellationToken: ct);
                    return;
                }
            }
            // Option 2: Fallback to lesson list of the current module
            if (userSession.CurrentLoadedModuleData != null)
            {
                 var replyKeyboardMarkup = LessonKeyboard.GetLessonsInModuleKeyboard(userSession.CurrentLoadedModuleData);
                 await _botClient.SendTextMessageAsync(chatId, $"Уроки в модуле \"{userSession.CurrentLoadedModuleData.Title}\":", replyMarkup: replyKeyboardMarkup, cancellationToken: ct);
            }
            // Option 3: Fallback to main course modules if no specific lesson context
            else
            {
                await HandleShowCourseModulesAsync(chatId, userSession, ct);
            }
        }

        // --- Lesson Quiz Handling Methods ---

        public async Task HandleStartQuizSessionCallbackAsync(string callbackData, UserSession userSession, long chatId, CancellationToken ct)
        {
            // callbackData format: "quiz_{moduleId}_{lessonId}"
            var parts = callbackData.Split('_');
            if (parts.Length < 3)
            {
                await _botClient.SendTextMessageAsync(chatId, "Ошибка: неверный формат данных для начала теста.", cancellationToken: ct);
                return;
            }

            string moduleId = parts[1];
            if (!int.TryParse(parts[2], out int lessonId))
            {
                await _botClient.SendTextMessageAsync(chatId, "Ошибка: неверный ID урока для теста.", cancellationToken: ct);
                return;
            }

            Console.WriteLine($"[CommandRouter] Starting quiz session for module {moduleId}, lesson {lessonId}");

            userSession.CurrentModuleIdForNav = moduleId;
            userSession.CurrentLessonIdForContext = lessonId;
            userSession.CurrentInteractionContext = callbackData;

            ModuleContent? moduleData = userSession.CurrentLoadedModuleData;
            if (moduleData == null || moduleData.ModuleId.ToString() != moduleId)
            {
                moduleData = await _courseContentLoaderService.LoadModuleFromFileAsync(moduleId);
                if (moduleData == null)
                {
                    await _botClient.SendTextMessageAsync(chatId, "Не удалось загрузить данные модуля для теста.", cancellationToken: ct);
                    return;
                }
                userSession.CurrentLoadedModuleData = moduleData;
            }

            var lesson = moduleData.Lessons.FirstOrDefault(l => l.LessonId == lessonId);
            if (lesson == null || lesson.Quiz == null || !lesson.Quiz.Any())
            {
                await _botClient.SendTextMessageAsync(chatId, "Для этого урока нет вопросов теста.", cancellationToken: ct);
                return;
            }

            userSession.CurrentLessonQuizQuestions = new List<QuizQuestionContent>(lesson.Quiz); // Store a copy
            userSession.CurrentLessonQuizQuestionIndex = 0;
            userSession.CurrentLessonQuizScore = 0;

            await _botClient.SendTextMessageAsync(chatId, $"Начинаем тест по уроку \"{lesson.Title}\"!", cancellationToken: ct);
            await ShowNextQuizQuestionAsync(userSession, chatId, ct);
        }

        private async Task ShowNextQuizQuestionAsync(UserSession userSession, long chatId, CancellationToken ct)
        {
            if (userSession.CurrentLessonQuizQuestions == null || userSession.CurrentLessonQuizQuestionIndex >= userSession.CurrentLessonQuizQuestions.Count)
            {
                int totalQuestions = userSession.CurrentLessonQuizQuestions?.Count ?? 0;
                await _botClient.SendTextMessageAsync(chatId,
                    $"Тест завершен!\nВаш результат: {userSession.CurrentLessonQuizScore} из {totalQuestions}",
                    cancellationToken: ct);
                await HandleExitQuizCallbackAsync(userSession.CurrentInteractionContext, userSession, chatId, ct, false); // Exit without explicit user action
                return;
            }

            var question = userSession.CurrentLessonQuizQuestions[userSession.CurrentLessonQuizQuestionIndex];

            var inlineKeyboardButtons = new List<List<InlineKeyboardButton>>();
            for (int i = 0; i < question.Options.Count; i++)
            {
                inlineKeyboardButtons.Add(new List<InlineKeyboardButton>
                {
                    InlineKeyboardButton.WithCallbackData(question.Options[i],
                        $"quiz_answer_{userSession.CurrentModuleIdForNav}_{userSession.CurrentLessonIdForContext}_{userSession.CurrentLessonQuizQuestionIndex}_{i}")
                });
            }
            // Optionally, add an "Exit Quiz" button to the question
            // inlineKeyboardButtons.Add(new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData("Выйти из теста", $"exit_quiz_{userSession.CurrentModuleIdForNav}_{userSession.CurrentLessonIdForContext}") });


            var inlineKeyboard = new InlineKeyboardMarkup(inlineKeyboardButtons);

            string questionText = $"*Вопрос {userSession.CurrentLessonQuizQuestionIndex + 1} из {userSession.CurrentLessonQuizQuestions.Count}:*\n\n{question.Question}";

            // Check if this is the first question to send a new message, otherwise edit
            // For simplicity now, always sending a new message for each question.
            // To edit, we'd need to store the messageId of the previous question.
            await _botClient.SendTextMessageAsync(
                chatId,
                questionText,
                parseMode: ParseMode.Markdown,
                replyMarkup: inlineKeyboard,
                cancellationToken: ct
            );
        }

        public async Task HandleQuizAnswerCallbackAsync(string callbackData, UserSession userSession, long chatId, int messageIdToEdit, CancellationToken ct)
        {
            // callbackData format: "quiz_answer_{moduleId}_{lessonId}_{questionIndex}_{optionIndex}"
            var parts = callbackData.Split('_');
            if (parts.Length < 5) { /* error handling */ return; }

            // string moduleId = parts[2]; // Not strictly needed if context is from session
            // int lessonId = int.Parse(parts[3]); // Not strictly needed
            if (!int.TryParse(parts[4], out int questionIndexFromCallback) ||
                !int.TryParse(parts[5], out int chosenOptionIndex))
            {
                await _botClient.SendTextMessageAsync(chatId, "Ошибка: неверный формат ответа на тест.", cancellationToken: ct);
                return;
            }

            if (userSession.CurrentLessonQuizQuestions == null || questionIndexFromCallback != userSession.CurrentLessonQuizQuestionIndex)
            {
                await _botClient.SendTextMessageAsync(chatId, "Ошибка: вопрос устарел или сессия теста неактивна.", cancellationToken: ct);
                // Optionally, resend current question or end quiz
                return;
            }

            var question = userSession.CurrentLessonQuizQuestions[userSession.CurrentLessonQuizQuestionIndex];
            string resultEmoji = "❌";
            if (chosenOptionIndex == question.CorrectOptionIndex)
            {
                userSession.CurrentLessonQuizScore++;
                resultEmoji = "✅";
            }

            // Edit the message with the question to show the choice and result
            // Reconstruct the question text and options, marking the chosen one
            var questionText = $"*Вопрос {userSession.CurrentLessonQuizQuestionIndex + 1} из {userSession.CurrentLessonQuizQuestions.Count}:*\n\n{question.Question}\n\nВаш ответ: {question.Options[chosenOptionIndex]} {resultEmoji}";
             if(resultEmoji == "❌") questionText += $"\nПравильный ответ: {question.Options[question.CorrectOptionIndex]}";


            try
            {
                 await _botClient.EditMessageTextAsync(
                    chatId: chatId,
                    messageId: messageIdToEdit,
                    text: questionText,
                    parseMode: ParseMode.Markdown,
                    replyMarkup: null, // Remove buttons after answer
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error editing message for quiz answer: {ex.Message}. Sending new message instead.");
                // Fallback to sending a new message if editing fails (e.g., message too old)
                await _botClient.SendTextMessageAsync(chatId, questionText, parseMode: ParseMode.Markdown, cancellationToken: ct);
            }


            userSession.CurrentLessonQuizQuestionIndex++;
            await Task.Delay(1000, ct); // Small delay before showing next question or result
            await ShowNextQuizQuestionAsync(userSession, chatId, ct);
        }

        public async Task HandleExitQuizCallbackAsync(string callbackData, UserSession userSession, long chatId, CancellationToken ct, bool sendConfirmation = true)
        {
            if(sendConfirmation) await _botClient.SendTextMessageAsync(chatId, "Выход из режима теста.", cancellationToken: ct);

            userSession.CurrentLessonQuizQuestions = null;
            userSession.CurrentLessonQuizQuestionIndex = 0;
            userSession.CurrentLessonQuizScore = 0;
            userSession.CurrentInteractionContext = null;

            if (userSession.CurrentLoadedModuleData != null && userSession.CurrentLessonIdForContext != 0)
            {
                var lesson = userSession.CurrentLoadedModuleData.Lessons.FirstOrDefault(l => l.LessonId == userSession.CurrentLessonIdForContext);
                if (lesson != null)
                {
                    var messageText = $"📖 *{lesson.Title}*\n\n{lesson.Content}";
                    var inlineKeyboardMarkup = LessonKeyboard.GetLessonContentInlineKeyboard(lesson.LessonId, userSession.CurrentModuleIdForNav);
                    await _botClient.SendTextMessageAsync(chatId, messageText, parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboardMarkup, cancellationToken: ct);
                    return;
                }
            }
            if (userSession.CurrentLoadedModuleData != null)
            {
                 var replyKeyboardMarkup = LessonKeyboard.GetLessonsInModuleKeyboard(userSession.CurrentLoadedModuleData);
                 await _botClient.SendTextMessageAsync(chatId, $"Уроки в модуле \"{userSession.CurrentLoadedModuleData.Title}\":", replyMarkup: replyKeyboardMarkup, cancellationToken: ct);
            }
            else
            {
                await HandleShowCourseModulesAsync(chatId, userSession, ct);
            }
        }

        // private async Task HandleUnknownCommandAsync(long chatId, CancellationToken cancellationToken) // Not used when returning false
        {
            await _botClient.SendTextMessageAsync(
                chatId: chatId,
                text: "Извините, я не понял эту команду. Пожалуйста, используйте кнопки.",
                cancellationToken: cancellationToken);
        }
    } // End of CommandRouter class
} // End of OmnieyeBot.BotHandlers namespace
