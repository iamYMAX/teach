using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using OmnieyeBot.Models;
using OmnieyeBot.Keyboards;
using Omnieye.Bot.States;
// Explicit using for services with aliases to avoid ambiguity
using BotCourseContentLoaderService = OmnieyeBot.Services.CourseContentLoaderService;
using ExistingUserSessionService = Omnieye.Bot.Services.UserSessionService;

namespace OmnieyeBot.BotHandlers
{
    public class CommandRouter
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ExistingUserSessionService _existingUserSessionService;
        private readonly BotCourseContentLoaderService _courseContentLoaderService;

        private CourseStructureRoot? _courseStructureRoot;

        public CommandRouter(ITelegramBotClient botClient,
                             ExistingUserSessionService userSessionService,
                             BotCourseContentLoaderService courseContentLoaderService)
        {
            _botClient = botClient;
            _existingUserSessionService = userSessionService;
            _courseContentLoaderService = courseContentLoaderService;
        }

        private async Task<CourseStructureRoot?> GetCourseStructureAsync(bool forceReload = false)
        {
            if (_courseStructureRoot == null || forceReload)
            {
                _courseStructureRoot = await _courseContentLoaderService.GetOrLoadCourseStructureAsync(forceReload);
            }
            return _courseStructureRoot;
        }

        public async Task<bool> RouteAsync(Message message, CancellationToken cancellationToken)
        {
            var chatId = message.Chat.Id;
            var messageText = message.Text;
            var userSession = _existingUserSessionService.GetUserSession(chatId);

            Console.WriteLine($"[CommandRouter] Routing message: '{messageText}' for chat {chatId}");

            if (messageText == "/start")
            {
                Console.WriteLine("[CommandRouter] Matched /start");
                await HandleShowLevelsAsync(chatId, userSession, cancellationToken, true);
                return true;
            }
            if (messageText == MainMenuKeyboard.LessonsButtonText)
            {
                Console.WriteLine($"[CommandRouter] Matched '{MainMenuKeyboard.LessonsButtonText}'");
                await HandleShowLevelsAsync(chatId, userSession, cancellationToken, true);
                return true;
            }
            if (messageText == LessonKeyboard.BackButtonText)
            {
                Console.WriteLine($"[CommandRouter] Matched '{LessonKeyboard.BackButtonText}'");
                await HandleBackCommandAsync(chatId, userSession, cancellationToken);
                return true;
            }
            if (messageText.StartsWith(LessonKeyboard.LevelPrefix))
            {
                Console.WriteLine($"[CommandRouter] Matched Level Prefix '{LessonKeyboard.LevelPrefix}'");
                await HandleShowModulesForLevelAsync(chatId, messageText, userSession, cancellationToken);
                return true;
            }
            if (messageText.StartsWith(LessonKeyboard.ModulePrefix))
            {
                Console.WriteLine($"[CommandRouter] Matched Module Prefix '{LessonKeyboard.ModulePrefix}'");
                await HandleShowLessonsInModuleAsync(chatId, messageText, userSession, cancellationToken);
                return true;
            }
            if (messageText.StartsWith(LessonKeyboard.LessonPrefix))
            {
                Console.WriteLine($"[CommandRouter] Matched Lesson Prefix '{LessonKeyboard.LessonPrefix}'");
                await HandleShowLessonContentAsync(chatId, messageText, userSession, cancellationToken);
                return true;
            }

            return false;
        }

        private async Task HandleShowLevelsAsync(long chatId, UserSession userSession, CancellationToken cancellationToken, bool isEntryPoint = false)
        {
            if (isEntryPoint)
            {
                userSession.CurrentLevelId = 0;
                userSession.CurrentModuleIdForNav = 0;
                userSession.CurrentLessonIdForContext = 0;
                userSession.CurrentLoadedModuleData = null;
                userSession.CurrentInteractionContext = null;
            }

            var courseData = await GetCourseStructureAsync();
            if (courseData == null || courseData.Levels == null || !courseData.Levels.Any())
            {
                await _botClient.SendTextMessageAsync(chatId, "Извините, учебные материалы временно недоступны.", cancellationToken: cancellationToken);
                return;
            }

            var replyKeyboardMarkup = LessonKeyboard.GetLevelsKeyboard(courseData.Levels);
            await _botClient.SendTextMessageAsync(chatId, "Выберите уровень обучения:", replyMarkup: replyKeyboardMarkup, cancellationToken: cancellationToken);
        }

        private async Task HandleShowModulesForLevelAsync(long chatId, string levelButtonText, UserSession userSession, CancellationToken cancellationToken)
        {
            var levelTitle = levelButtonText.Replace(LessonKeyboard.LevelPrefix, "").Trim();
            var courseData = await GetCourseStructureAsync();
            if (courseData == null) { await _botClient.SendTextMessageAsync(chatId, "Ошибка загрузки данных курса.", cancellationToken: cancellationToken); return; }

            var selectedLevel = courseData.Levels.FirstOrDefault(l => l.Title == levelTitle);
            if (selectedLevel == null)
            {
                await _botClient.SendTextMessageAsync(chatId, "Выбранный уровень не найден.", cancellationToken: cancellationToken);
                await HandleShowLevelsAsync(chatId, userSession, cancellationToken);
                return;
            }

            userSession.CurrentLevelId = selectedLevel.LevelId;
            userSession.CurrentModuleIdForNav = 0;
            userSession.CurrentLessonIdForContext = 0;
            userSession.CurrentLoadedModuleData = null;

            if (selectedLevel.Modules == null || !selectedLevel.Modules.Any())
            {
                 await _botClient.SendTextMessageAsync(chatId, $"В уровне \"{selectedLevel.Title}\" пока нет модулей.", cancellationToken: cancellationToken);
                 await HandleShowLevelsAsync(chatId, userSession, cancellationToken);
                 return;
            }

            var replyKeyboardMarkup = LessonKeyboard.GetModulesInLevelKeyboard(selectedLevel);
            await _botClient.SendTextMessageAsync(chatId, $"Модули уровня \"{selectedLevel.Title}\":", replyMarkup: replyKeyboardMarkup, cancellationToken: cancellationToken);
        }

        private async Task HandleShowLessonsInModuleAsync(long chatId, string moduleButtonText, UserSession userSession, CancellationToken cancellationToken)
        {
            if (userSession.CurrentLevelId == 0)
            {
                await _botClient.SendTextMessageAsync(chatId, "Сначала выберите уровень.", cancellationToken: cancellationToken);
                await HandleShowLevelsAsync(chatId, userSession, cancellationToken);
                return;
            }

            var moduleTitle = moduleButtonText.Replace(LessonKeyboard.ModulePrefix, "").Trim();
            var courseData = await GetCourseStructureAsync();
            if (courseData == null) { await _botClient.SendTextMessageAsync(chatId, "Ошибка загрузки данных курса.", cancellationToken: cancellationToken); return; }

            var selectedLevel = courseData.Levels.FirstOrDefault(l => l.LevelId == userSession.CurrentLevelId);
            if (selectedLevel == null) { await _botClient.SendTextMessageAsync(chatId, "Текущий уровень не найден.", cancellationToken: cancellationToken); await HandleShowLevelsAsync(chatId, userSession, cancellationToken); return; }

            var selectedModule = selectedLevel.Modules.FirstOrDefault(m => m.Title == moduleTitle);
            if (selectedModule == null)
            {
                await _botClient.SendTextMessageAsync(chatId, "Выбранный модуль не найден.", cancellationToken: cancellationToken);
                await HandleShowModulesForLevelAsync(chatId, $"{LessonKeyboard.LevelPrefix}{selectedLevel.Title}", userSession, cancellationToken);
                return;
            }

            userSession.CurrentModuleIdForNav = selectedModule.ModuleId;
            userSession.CurrentLoadedModuleData = selectedModule;
            userSession.CurrentLessonIdForContext = 0;
            Console.WriteLine($"[CommandRouter] In HandleShowLessonsInModuleAsync: Set LevelId='{userSession.CurrentLevelId}', ModuleId='{userSession.CurrentModuleIdForNav}', LoadedModule='{selectedModule.Title}'");

            if (selectedModule.Lessons == null || !selectedModule.Lessons.Any())
            {
                await _botClient.SendTextMessageAsync(chatId, $"В модуле \"{selectedModule.Title}\" пока нет уроков.", cancellationToken: cancellationToken);
                await HandleShowModulesForLevelAsync(chatId, $"{LessonKeyboard.LevelPrefix}{selectedLevel.Title}", userSession, cancellationToken);
                return;
            }

            var replyKeyboardMarkup = LessonKeyboard.GetLessonsInModuleKeyboard(selectedModule);
            await _botClient.SendTextMessageAsync(chatId, $"Уроки в модуле \"{selectedModule.Title}\":", replyMarkup: replyKeyboardMarkup, cancellationToken: cancellationToken);
        }

        private async Task HandleShowLessonContentAsync(long chatId, string lessonButtonText, UserSession userSession, CancellationToken cancellationToken)
        {
            if (userSession.CurrentLevelId == 0 || userSession.CurrentModuleIdForNav == 0 || userSession.CurrentLoadedModuleData == null)
            {
                await _botClient.SendTextMessageAsync(chatId, "Ошибка: Уровень или модуль не выбраны. Пожалуйста, начните с выбора уровня.", cancellationToken: cancellationToken);
                await HandleShowLevelsAsync(chatId, userSession, cancellationToken, true);
                return;
            }

            var lessonIdString = Regex.Match(lessonButtonText, @"\(id:(\d+)\)").Groups[1].Value;
            if (!int.TryParse(lessonIdString, out int lessonId))
            {
                await _botClient.SendTextMessageAsync(chatId, "Ошибка: Не удалось распознать ID урока.", cancellationToken: cancellationToken);
                await _botClient.SendTextMessageAsync(chatId, $"Уроки в модуле \"{userSession.CurrentLoadedModuleData.Title}\":",
                    replyMarkup: LessonKeyboard.GetLessonsInModuleKeyboard(userSession.CurrentLoadedModuleData), cancellationToken: cancellationToken);
                return;
            }

            var lesson = userSession.CurrentLoadedModuleData.Lessons.FirstOrDefault(l => l.LessonId == lessonId);
            if (lesson == null)
            {
                await _botClient.SendTextMessageAsync(chatId, "Выбранный урок не найден.", cancellationToken: cancellationToken);
                await _botClient.SendTextMessageAsync(chatId, $"Уроки в модуле \"{userSession.CurrentLoadedModuleData.Title}\":",
                    replyMarkup: LessonKeyboard.GetLessonsInModuleKeyboard(userSession.CurrentLoadedModuleData), cancellationToken: cancellationToken);
                return;
            }

            userSession.CurrentLessonIdForContext = lesson.LessonId;
            Console.WriteLine($"[CommandRouter] Showing content for Lvl:{userSession.CurrentLevelId}, Mod:{userSession.CurrentModuleIdForNav}, Les:{lesson.LessonId}");

            var messageText = $"📖 *{lesson.Title}*\n\n{lesson.Content}";
            var inlineKeyboardMarkup = LessonKeyboard.GetLessonContentInlineKeyboard(userSession.CurrentLevelId, userSession.CurrentModuleIdForNav, lesson.LessonId);

            await _botClient.SendTextMessageAsync(chatId, messageText, parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboardMarkup, cancellationToken: cancellationToken);
        }

        private async Task HandleBackCommandAsync(long chatId, UserSession userSession, CancellationToken cancellationToken)
        {
            var courseData = await GetCourseStructureAsync(); // Needed for titles
            if (courseData == null) { /* Error handling */ await _botClient.SendTextMessageAsync(chatId, "Ошибка загрузки данных курса для навигации.", cancellationToken: cancellationToken); return; }

            if (userSession.CurrentInteractionContext != null) // If in flashcard or quiz session
            {
                // Exit flashcard/quiz and return to lesson content
                if (userSession.CurrentInteractionContext.StartsWith("flashcard_session_"))
                {
                    await HandleExitFlashcardsCallbackAsync(userSession.CurrentInteractionContext, userSession, chatId, cancellationToken);
                }
                else if (userSession.CurrentInteractionContext.StartsWith("quiz_session_"))
                {
                    await HandleExitQuizCallbackAsync(userSession.CurrentInteractionContext, userSession, chatId, cancellationToken);
                }
                else // Unknown interaction, clear and go to lesson
                {
                    userSession.CurrentInteractionContext = null;
                    // Proceed to logic below to determine lesson/module/level
                }
                // After exiting interaction, CurrentLessonIdForContext should still be set, so next "Back" will go to lesson list.
                // The exit methods themselves should handle returning to the lesson view.
                return;
            }

            if (userSession.CurrentLessonIdForContext != 0)
            {
                userSession.CurrentLessonIdForContext = 0;
                userSession.CurrentInteractionContext = null;

                if (userSession.CurrentLoadedModuleData != null)
                {
                    await _botClient.SendTextMessageAsync(chatId, $"Уроки в модуле \"{userSession.CurrentLoadedModuleData.Title}\":",
                        replyMarkup: LessonKeyboard.GetLessonsInModuleKeyboard(userSession.CurrentLoadedModuleData), cancellationToken: cancellationToken);
                }
                else if (userSession.CurrentLevelId != 0 && userSession.CurrentModuleIdForNav != 0) // Attempt to reconstruct module context
                {
                    var level = courseData.Levels.FirstOrDefault(l => l.LevelId == userSession.CurrentLevelId);
                    var module = level?.Modules.FirstOrDefault(m => m.ModuleId == userSession.CurrentModuleIdForNav);
                    if (module != null) {
                        userSession.CurrentLoadedModuleData = module; // Re-cache
                         await _botClient.SendTextMessageAsync(chatId, $"Уроки в модуле \"{module.Title}\":",
                            replyMarkup: LessonKeyboard.GetLessonsInModuleKeyboard(module), cancellationToken: cancellationToken);
                    } else { await HandleShowLevelsAsync(chatId, userSession, cancellationToken); }
                } else {
                     await HandleShowLevelsAsync(chatId, userSession, cancellationToken);
                }
            }
            else if (userSession.CurrentModuleIdForNav != 0)
            {
                userSession.CurrentModuleIdForNav = 0;
                userSession.CurrentLoadedModuleData = null;
                if (userSession.CurrentLevelId != 0)
                {
                    var level = courseData.Levels.FirstOrDefault(l => l.LevelId == userSession.CurrentLevelId);
                    if (level != null) {
                         await _botClient.SendTextMessageAsync(chatId, $"Модули уровня \"{level.Title}\":",
                            replyMarkup: LessonKeyboard.GetModulesInLevelKeyboard(level), cancellationToken: cancellationToken);
                    } else {
                        await HandleShowLevelsAsync(chatId, userSession, cancellationToken);
                    }
                } else {
                     await HandleShowLevelsAsync(chatId, userSession, cancellationToken);
                }
            }
            else if (userSession.CurrentLevelId != 0)
            {
                userSession.CurrentLevelId = 0;
                await HandleShowLevelsAsync(chatId, userSession, cancellationToken);
            }
            else
            {
                await HandleShowLevelsAsync(chatId, userSession, cancellationToken, true);
            }
        }

        // --- Flashcard Handling Methods ---
        public async Task HandleStartFlashcardSessionCallbackAsync(string callbackData, UserSession userSession, long chatId, CancellationToken ct)
        {
            var parts = callbackData.Split('_');
            if (parts.Length < 4) { await _botClient.SendTextMessageAsync(chatId, "Ошибка callback для флеш-карт (недостаточно частей).", cancellationToken: ct); return; }
            if (!int.TryParse(parts[1], out int levelId) ||
                !int.TryParse(parts[2], out int moduleId) ||
                !int.TryParse(parts[3], out int lessonId)) { await _botClient.SendTextMessageAsync(chatId, "Ошибка callback для флеш-карт (неверные ID).", cancellationToken: ct); return; }

            Console.WriteLine($"[CBRouter] Start Flashcards: Lvl:{levelId},Mod:{moduleId},Les:{lessonId}");
            var courseData = await GetCourseStructureAsync();
            var lesson = courseData?.Levels.FirstOrDefault(l => l.LevelId == levelId)?
                                 .Modules.FirstOrDefault(m => m.ModuleId == moduleId)?
                                 .Lessons.FirstOrDefault(le => le.LessonId == lessonId);

            if (lesson == null || lesson.Flashcards == null || !lesson.Flashcards.Any())
            {
                // Attempt to answer callback query even on error to remove loading state
                var callbackQueryId = userSession.CurrentInteractionContext?.Split('|').LastOrDefault(); // Assuming we store it like "type|queryId"
                if (!string.IsNullOrEmpty(callbackQueryId)) await _botClient.AnswerCallbackQueryAsync(callbackQueryId, "Для этого урока нет флеш-карточек.", showAlert:true, cancellationToken: ct);
                else await _botClient.SendTextMessageAsync(chatId, "Для этого урока нет флеш-карточек.", cancellationToken: ct);
                return;
            }

            userSession.CurrentLevelId = levelId;
            userSession.CurrentModuleIdForNav = moduleId;
            userSession.CurrentLessonIdForContext = lessonId; // Set this for context
            userSession.CurrentInteractionContext = $"flashcard_session_{levelId}_{moduleId}_{lessonId}";
            userSession.CurrentLoadedModuleData = courseData?.Levels.FirstOrDefault(l => l.LevelId == levelId)?.Modules.FirstOrDefault(m => m.ModuleId == moduleId);

            var random = new Random();
            userSession.CurrentFlashcardContentQueue = new Queue<FlashcardContent>(lesson.Flashcards.OrderBy(f => random.Next()));
            userSession.CurrentFlashcardContent = null;

            // Remove previous inline keyboard if any by sending a new message or editing.
            // For simplicity, send new message.
            await _botClient.SendTextMessageAsync(chatId, "Начинаем сессию флеш-карточек!", cancellationToken: ct);
            await ShowNextFlashcardAsync(userSession, chatId, ct);
        }

        private async Task ShowNextFlashcardAsync(UserSession userSession, long chatId, CancellationToken ct)
        {
            if (userSession.CurrentFlashcardContentQueue == null || !userSession.CurrentFlashcardContentQueue.Any())
            {
                await _botClient.SendTextMessageAsync(chatId, "✅ Все карточки просмотрены!", cancellationToken: ct);
                await HandleExitFlashcardsCallbackAsync(userSession.CurrentInteractionContext ?? "", userSession, chatId, ct, false);
                return;
            }

            var card = userSession.CurrentFlashcardContentQueue.Dequeue();
            userSession.CurrentFlashcardContent = card;

            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new [] { InlineKeyboardButton.WithCallbackData("Показать ответ", $"show_answer_{userSession.CurrentLevelId}_{userSession.CurrentModuleIdForNav}_{userSession.CurrentLessonIdForContext}") },
                new [] { InlineKeyboardButton.WithCallbackData("Следующая карточка", $"next_flashcard_{userSession.CurrentLevelId}_{userSession.CurrentModuleIdForNav}_{userSession.CurrentLessonIdForContext}") },
                new [] { InlineKeyboardButton.WithCallbackData("Выйти из карточек", $"exit_flashcards_{userSession.CurrentLevelId}_{userSession.CurrentModuleIdForNav}_{userSession.CurrentLessonIdForContext}") }
            });

            await _botClient.SendTextMessageAsync(chatId, $"❓ *Вопрос:*\n{card.Question}", parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboard, cancellationToken: ct);
        }

        public async Task HandleShowAnswerCallbackAsync(string callbackData, UserSession userSession, long chatId, int messageId, CancellationToken ct)
        {
            if (userSession.CurrentFlashcardContent == null) { await _botClient.SendTextMessageAsync(chatId, "Ошибка: нет активной карточки.", cancellationToken: ct); return; }

            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new [] { InlineKeyboardButton.WithCallbackData("Следующая карточка", $"next_flashcard_{userSession.CurrentLevelId}_{userSession.CurrentModuleIdForNav}_{userSession.CurrentLessonIdForContext}") },
                new [] { InlineKeyboardButton.WithCallbackData("Выйти из карточек", $"exit_flashcards_{userSession.CurrentLevelId}_{userSession.CurrentModuleIdForNav}_{userSession.CurrentLessonIdForContext}") }
            });

            string fullMessageText = $"❓ *Вопрос:*\n{userSession.CurrentFlashcardContent.Question}\n\n💡 *Ответ:*\n{userSession.CurrentFlashcardContent.Answer}";
            try
            {
                await _botClient.EditMessageTextAsync(chatId, messageId, fullMessageText, parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboard, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CBRouter] Error editing flashcard answer: {ex.Message}. Sending new msg.");
                await _botClient.SendTextMessageAsync(chatId, $"💡 *Ответ:*\n{userSession.CurrentFlashcardContent.Answer}", parseMode: ParseMode.Markdown, cancellationToken: ct);
                // Re-send the question with next/exit buttons if edit failed and we sent answer as new.
                 var originalQuestionKeyboard = new InlineKeyboardMarkup(new[]
                {
                    new [] { InlineKeyboardButton.WithCallbackData("Следующая карточка", $"next_flashcard_{userSession.CurrentLevelId}_{userSession.CurrentModuleIdForNav}_{userSession.CurrentLessonIdForContext}") },
                    new [] { InlineKeyboardButton.WithCallbackData("Выйти из карточек", $"exit_flashcards_{userSession.CurrentLevelId}_{userSession.CurrentModuleIdForNav}_{userSession.CurrentLessonIdForContext}") }
                });
                await _botClient.SendTextMessageAsync(chatId, $"❓ *Вопрос (повтор):*\n{userSession.CurrentFlashcardContent.Question}", parseMode: ParseMode.Markdown, replyMarkup: originalQuestionKeyboard, cancellationToken: ct);
            }
        }

        public async Task HandleNextFlashcardCallbackAsync(string callbackData, UserSession userSession, long chatId, CancellationToken ct)
        {
            await ShowNextFlashcardAsync(userSession, chatId, ct);
        }

        public async Task HandleExitFlashcardsCallbackAsync(string callbackData, UserSession userSession, long chatId, CancellationToken ct, bool sendConfirmation = true)
        {
            if(sendConfirmation) await _botClient.SendTextMessageAsync(chatId, "Выход из режима флеш-карточек.", cancellationToken: ct);
            var previousInteractionContext = userSession.CurrentInteractionContext; // Store before clearing
            userSession.CurrentFlashcardContentQueue = null;
            userSession.CurrentFlashcardContent = null;
            userSession.CurrentInteractionContext = null;

            var courseData = await GetCourseStructureAsync();
            var lesson = courseData?.Levels.FirstOrDefault(l => l.LevelId == userSession.CurrentLevelId)?
                                 .Modules.FirstOrDefault(m => m.ModuleId == userSession.CurrentModuleIdForNav)?
                                 .Lessons.FirstOrDefault(le => le.LessonId == userSession.CurrentLessonIdForContext);
            if (lesson != null)
            {
                var messageText = $"📖 *{lesson.Title}*\n\n{lesson.Content}";
                var inlineKeyboardMarkup = LessonKeyboard.GetLessonContentInlineKeyboard(userSession.CurrentLevelId, userSession.CurrentModuleIdForNav, lesson.LessonId);
                await _botClient.SendTextMessageAsync(chatId, messageText, parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboardMarkup, cancellationToken: ct);
            } else {
                await HandleShowLevelsAsync(chatId, userSession, cancellationToken, true); // Go to top if context lost
            }
        }

        // --- Lesson Quiz Handling Methods ---
        public async Task HandleStartQuizSessionCallbackAsync(string callbackData, UserSession userSession, long chatId, CancellationToken ct)
        {
            var parts = callbackData.Split('_');
            if (parts.Length < 4) { await _botClient.SendTextMessageAsync(chatId, "Ошибка callback для квиза (недостаточно частей).", cancellationToken: ct); return; }
             if (!int.TryParse(parts[1], out int levelId) ||
                !int.TryParse(parts[2], out int moduleId) ||
                !int.TryParse(parts[3], out int lessonId)) { await _botClient.SendTextMessageAsync(chatId, "Ошибка callback для квиза (неверные ID).", cancellationToken: ct); return; }

            Console.WriteLine($"[CBRouter] Start Quiz: Lvl:{levelId},Mod:{moduleId},Les:{lessonId}");
            var courseData = await GetCourseStructureAsync();
            var lesson = courseData?.Levels.FirstOrDefault(l => l.LevelId == levelId)?
                                 .Modules.FirstOrDefault(m => m.ModuleId == moduleId)?
                                 .Lessons.FirstOrDefault(le => le.LessonId == lessonId);

            if (lesson == null || lesson.Quiz == null || !lesson.Quiz.Any())
            {
                var callbackQueryId = userSession.CurrentInteractionContext?.Split('|').LastOrDefault(); // Assuming we store it like "type|queryId"
                if (!string.IsNullOrEmpty(callbackQueryId)) await _botClient.AnswerCallbackQueryAsync(callbackQueryId, "Для этого урока нет вопросов теста.", showAlert:true, cancellationToken: ct);
                else await _botClient.SendTextMessageAsync(chatId, "Для этого урока нет вопросов теста.", cancellationToken: ct);
                return;
            }

            userSession.CurrentLevelId = levelId;
            userSession.CurrentModuleIdForNav = moduleId;
            userSession.CurrentLessonIdForContext = lessonId;
            userSession.CurrentInteractionContext = $"quiz_session_{levelId}_{moduleId}_{lessonId}";
            userSession.CurrentLoadedModuleData = courseData?.Levels.FirstOrDefault(l => l.LevelId == levelId)?.Modules.FirstOrDefault(m => m.ModuleId == moduleId);

            userSession.CurrentLessonQuizQuestions = new List<QuizQuestionContent>(lesson.Quiz);
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
                await _botClient.SendTextMessageAsync(chatId, $"Тест завершен!\nВаш результат: {userSession.CurrentLessonQuizScore} из {totalQuestions}", cancellationToken: ct);
                await HandleExitQuizCallbackAsync(userSession.CurrentInteractionContext ?? "", userSession, chatId, ct, false);
                return;
            }

            var question = userSession.CurrentLessonQuizQuestions[userSession.CurrentLessonQuizQuestionIndex];
            var inlineKeyboardButtons = new List<List<InlineKeyboardButton>>();
            for (int i = 0; i < question.Options.Count; i++)
            {
                inlineKeyboardButtons.Add(new List<InlineKeyboardButton> {
                    InlineKeyboardButton.WithCallbackData(question.Options[i],
                        $"quiz_answer_{userSession.CurrentLevelId}_{userSession.CurrentModuleIdForNav}_{userSession.CurrentLessonIdForContext}_{userSession.CurrentLessonQuizQuestionIndex}_{i}")
                });
            }
            var inlineKeyboard = new InlineKeyboardMarkup(inlineKeyboardButtons);
            string questionText = $"*Вопрос {userSession.CurrentLessonQuizQuestionIndex + 1} из {userSession.CurrentLessonQuizQuestions.Count}:*\n\n{question.Question}";

            await _botClient.SendTextMessageAsync(chatId, questionText, parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboard, cancellationToken: ct);
        }

        public async Task HandleQuizAnswerCallbackAsync(string callbackData, UserSession userSession, long chatId, int messageIdToEdit, CancellationToken ct)
        {
            var parts = callbackData.Split('_');
            if (parts.Length < 6) { await _botClient.SendTextMessageAsync(chatId, "Ошибка callback ответа на квиз (недостаточно частей).", cancellationToken: ct); return; }
            if (!int.TryParse(parts[4], out int questionIndexFromCallback) ||
                !int.TryParse(parts[5], out int chosenOptionIndex)) { await _botClient.SendTextMessageAsync(chatId, "Ошибка callback ответа на квиз (неверные ID).", cancellationToken: ct); return; }

            if (userSession.CurrentLessonQuizQuestions == null || questionIndexFromCallback != userSession.CurrentLessonQuizQuestionIndex)
            { await _botClient.SendTextMessageAsync(chatId, "Ошибка: вопрос устарел или сессия теста неактивна.", cancellationToken: ct); return; }

            var question = userSession.CurrentLessonQuizQuestions[userSession.CurrentLessonQuizQuestionIndex];
            string resultEmoji = "❌";
            if (chosenOptionIndex == question.CorrectOptionIndex)
            {
                userSession.CurrentLessonQuizScore++;
                resultEmoji = "✅";
            }

            var questionText = $"*Вопрос {userSession.CurrentLessonQuizQuestionIndex + 1} из {userSession.CurrentLessonQuizQuestions.Count}:*\n\n{question.Question}\n\nВаш ответ: {question.Options[chosenOptionIndex]} {resultEmoji}";
            if(resultEmoji == "❌") questionText += $"\nПравильный ответ: {question.Options[question.CorrectOptionIndex]}";

            try
            {
                 await _botClient.EditMessageTextAsync(chatId, messageIdToEdit, questionText, parseMode: ParseMode.Markdown, replyMarkup: null, cancellationToken: ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CBRouter] Error editing quiz answer: {ex.Message}. Sending new msg.");
                await _botClient.SendTextMessageAsync(chatId, questionText, parseMode: ParseMode.Markdown, cancellationToken: ct);
            }

            userSession.CurrentLessonQuizQuestionIndex++;
            await Task.Delay(1000, ct);
            await ShowNextQuizQuestionAsync(userSession, chatId, ct);
        }

        public async Task HandleExitQuizCallbackAsync(string callbackData, UserSession userSession, long chatId, CancellationToken ct, bool sendConfirmation = true)
        {
            if(sendConfirmation) await _botClient.SendTextMessageAsync(chatId, "Выход из режима теста.", cancellationToken: ct);
            userSession.CurrentLessonQuizQuestions = null;
            userSession.CurrentLessonQuizQuestionIndex = 0;
            userSession.CurrentLessonQuizScore = 0;
            userSession.CurrentInteractionContext = null;

            var courseData = await GetCourseStructureAsync();
            var lesson = courseData?.Levels.FirstOrDefault(l => l.LevelId == userSession.CurrentLevelId)?
                                 .Modules.FirstOrDefault(m => m.ModuleId == userSession.CurrentModuleIdForNav)?
                                 .Lessons.FirstOrDefault(le => le.LessonId == userSession.CurrentLessonIdForContext);
            if (lesson != null)
            {
                var messageText = $"📖 *{lesson.Title}*\n\n{lesson.Content}";
                var inlineKeyboardMarkup = LessonKeyboard.GetLessonContentInlineKeyboard(userSession.CurrentLevelId, userSession.CurrentModuleIdForNav, lesson.LessonId);
                await _botClient.SendTextMessageAsync(chatId, messageText, parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboardMarkup, cancellationToken: ct);
            } else {
                await HandleShowLevelsAsync(chatId, userSession, cancellationToken, true); // Go to top if context lost
            }
        }
    }
}

[end of bot/BotHandlers/CommandRouter.cs]
