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
using OmnieyeBot.Services;
using Omnieye.Bot.Services;

namespace OmnieyeBot.BotHandlers
{
    public class CommandRouter
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ExistingUserSessionService _existingUserSessionService; // Renamed for clarity
        private readonly CourseContentLoaderService _courseContentLoaderService;

        private CourseStructureRoot? _courseStructureRoot; // Cache for the entire course structure

        public CommandRouter(ITelegramBotClient botClient,
                             ExistingUserSessionService userSessionService,
                             CourseContentLoaderService courseContentLoaderService)
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
                await HandleShowLevelsAsync(chatId, userSession, cancellationToken, true); // true to force reload/reset
                return true;
            }
            if (messageText == MainMenuKeyboard.LessonsButtonText)
            {
                Console.WriteLine($"[CommandRouter] Matched '{MainMenuKeyboard.LessonsButtonText}'");
                await HandleShowLevelsAsync(chatId, userSession, cancellationToken, true); // true to force reload/reset
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

            return false; // Command not handled by this router
        }

        // Renamed from HandleStartCommandAsync to reflect it now shows levels
        private async Task HandleShowLevelsAsync(long chatId, UserSession userSession, CancellationToken cancellationToken, bool isEntryPoint = false)
        {
            if (isEntryPoint) // Reset navigation only if this is a top-level entry (like /start or "Уроки")
            {
                userSession.CurrentLevelId = 0;
                userSession.CurrentModuleIdForNav = 0;
                userSession.CurrentLessonIdForContext = 0;
                userSession.CurrentLoadedModuleData = null;
                userSession.CurrentInteractionContext = null; // Clear interaction context too
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
            if (courseData == null) { /* error */ return; }

            var selectedLevel = courseData.Levels.FirstOrDefault(l => l.Title == levelTitle);
            if (selectedLevel == null)
            {
                await _botClient.SendTextMessageAsync(chatId, "Выбранный уровень не найден.", cancellationToken: cancellationToken);
                await HandleShowLevelsAsync(chatId, userSession, cancellationToken); // Show levels again
                return;
            }

            userSession.CurrentLevelId = selectedLevel.LevelId;
            userSession.CurrentModuleIdForNav = 0; // Reset module
            userSession.CurrentLessonIdForContext = 0; // Reset lesson
            userSession.CurrentLoadedModuleData = null; // Clear old module data

            if (selectedLevel.Modules == null || !selectedLevel.Modules.Any())
            {
                 await _botClient.SendTextMessageAsync(chatId, $"В уровне \"{selectedLevel.Title}\" пока нет модулей.", cancellationToken: cancellationToken);
                 await HandleShowLevelsAsync(chatId, userSession, cancellationToken); // Show levels again
                 return;
            }

            var replyKeyboardMarkup = LessonKeyboard.GetModulesInLevelKeyboard(selectedLevel);
            await _botClient.SendTextMessageAsync(chatId, $"Модули уровня \"{selectedLevel.Title}\":", replyMarkup: replyKeyboardMarkup, cancellationToken: cancellationToken);
        }

        private async Task HandleShowLessonsInModuleAsync(long chatId, string moduleButtonText, UserSession userSession, CancellationToken cancellationToken)
        {
            if (userSession.CurrentLevelId == 0) // Level must be selected first
            {
                await _botClient.SendTextMessageAsync(chatId, "Сначала выберите уровень.", cancellationToken: cancellationToken);
                await HandleShowLevelsAsync(chatId, userSession, cancellationToken);
                return;
            }

            var moduleTitle = moduleButtonText.Replace(LessonKeyboard.ModulePrefix, "").Trim();
            var courseData = await GetCourseStructureAsync();
            if (courseData == null) { /* error */ return; }

            var selectedLevel = courseData.Levels.FirstOrDefault(l => l.LevelId == userSession.CurrentLevelId);
            if (selectedLevel == null) { /* error, level not found */ await HandleShowLevelsAsync(chatId, userSession, cancellationToken); return; }

            var selectedModule = selectedLevel.Modules.FirstOrDefault(m => m.Title == moduleTitle);
            if (selectedModule == null)
            {
                await _botClient.SendTextMessageAsync(chatId, "Выбранный модуль не найден.", cancellationToken: cancellationToken);
                await HandleShowModulesForLevelAsync(chatId, $"{LessonKeyboard.LevelPrefix}{selectedLevel.Title}", userSession, cancellationToken); // Show modules for current level
                return;
            }

            userSession.CurrentModuleIdForNav = selectedModule.ModuleId;
            userSession.CurrentLoadedModuleData = selectedModule; // Cache the whole module
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
                // Show lessons for the current module again
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
            if (userSession.CurrentLessonIdForContext != 0) // Was viewing lesson content or its activities (flashcards/quiz)
            {
                userSession.CurrentLessonIdForContext = 0; // Clear lesson context
                userSession.CurrentInteractionContext = null; // Clear interaction context (flashcards/quiz)

                // Go back to lesson list of the current module
                if (userSession.CurrentLoadedModuleData != null) // Module data should be cached
                {
                    await _botClient.SendTextMessageAsync(chatId, $"Уроки в модуле \"{userSession.CurrentLoadedModuleData.Title}\":",
                        replyMarkup: LessonKeyboard.GetLessonsInModuleKeyboard(userSession.CurrentLoadedModuleData), cancellationToken: cancellationToken);
                }
                // If module data somehow lost, try to go up to module list for current level
                else if (userSession.CurrentLevelId != 0)
                {
                    var courseData = await GetCourseStructureAsync();
                    var level = courseData?.Levels.FirstOrDefault(l => l.LevelId == userSession.CurrentLevelId);
                    if (level != null) {
                         await _botClient.SendTextMessageAsync(chatId, $"Модули уровня \"{level.Title}\":",
                            replyMarkup: LessonKeyboard.GetModulesInLevelKeyboard(level), cancellationToken: cancellationToken);
                    } else { // Fallback further
                        await HandleShowLevelsAsync(chatId, userSession, cancellationToken);
                    }
                } else { // Fallback to top
                     await HandleShowLevelsAsync(chatId, userSession, cancellationToken);
                }
            }
            else if (userSession.CurrentModuleIdForNav != 0) // Was viewing lesson list for a module
            {
                userSession.CurrentModuleIdForNav = 0; // Clear module context
                userSession.CurrentLoadedModuleData = null;
                // Go back to module list of the current level
                if (userSession.CurrentLevelId != 0)
                {
                     var courseData = await GetCourseStructureAsync();
                    var level = courseData?.Levels.FirstOrDefault(l => l.LevelId == userSession.CurrentLevelId);
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
            else if (userSession.CurrentLevelId != 0) // Was viewing module list for a level
            {
                userSession.CurrentLevelId = 0; // Clear level context
                await HandleShowLevelsAsync(chatId, userSession, cancellationToken); // Go back to level list
            }
            else // Was viewing level list (or at main menu and pressed back)
            {
                // No higher state in this new navigation, so effectively "main menu" of courses is level list.
                // Or, if "Back" from level list should go to original bot's main menu, then return false.
                // For now, "Back" from level list re-shows level list or could show a top-level course message.
                await HandleShowLevelsAsync(chatId, userSession, cancellationToken, true); // Re-show levels, reset all context
            }
        }

        // --- Flashcard Handling Methods ---
        public async Task HandleStartFlashcardSessionCallbackAsync(string callbackData, UserSession userSession, long chatId, CancellationToken ct)
        {
            var parts = callbackData.Split('_'); // Expected: flashcards_{levelId}_{moduleId}_{lessonId}
            if (parts.Length < 4) { /* error */ return; }
            if (!int.TryParse(parts[1], out int levelId) ||
                !int.TryParse(parts[2], out int moduleId) ||
                !int.TryParse(parts[3], out int lessonId)) { /* error */ return; }

            Console.WriteLine($"[CBRouter] Start Flashcards: Lvl:{levelId},Mod:{moduleId},Les:{lessonId}");
            var courseData = await GetCourseStructureAsync();
            var lesson = courseData?.Levels.FirstOrDefault(l => l.LevelId == levelId)?
                                 .Modules.FirstOrDefault(m => m.ModuleId == moduleId)?
                                 .Lessons.FirstOrDefault(le => le.LessonId == lessonId);

            if (lesson == null || lesson.Flashcards == null || !lesson.Flashcards.Any())
            {
                await _botClient.AnswerCallbackQueryAsync(userSession.CurrentInteractionContext.Split('|')[0], "Для этого урока нет флеш-карточек.", showAlert:true, cancellationToken: ct); // Use actual callbackQueryId
                return;
            }

            userSession.CurrentLevelId = levelId;
            userSession.CurrentModuleIdForNav = moduleId;
            userSession.CurrentLessonIdForContext = lessonId;
            userSession.CurrentInteractionContext = $"flashcard_session_{levelId}_{moduleId}_{lessonId}"; // Context for this session
            // It's good to also cache the specific ModuleContent if not already done when navigating to lesson
            userSession.CurrentLoadedModuleData = courseData?.Levels.FirstOrDefault(l => l.LevelId == levelId)?.Modules.FirstOrDefault(m => m.ModuleId == moduleId);


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
            if (userSession.CurrentFlashcardContent == null) { /* error */ return; }

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
            }
        }

        public async Task HandleNextFlashcardCallbackAsync(string callbackData, UserSession userSession, long chatId, CancellationToken ct)
        {
            await ShowNextFlashcardAsync(userSession, chatId, ct);
        }

        public async Task HandleExitFlashcardsCallbackAsync(string callbackData, UserSession userSession, long chatId, CancellationToken ct, bool sendConfirmation = true)
        {
            if(sendConfirmation) await _botClient.SendTextMessageAsync(chatId, "Выход из режима флеш-карточек.", cancellationToken: ct);
            userSession.CurrentFlashcardContentQueue = null;
            userSession.CurrentFlashcardContent = null;
            userSession.CurrentInteractionContext = null;

            // Try to return to lesson content
            var courseData = await GetCourseStructureAsync();
            var lesson = courseData?.Levels.FirstOrDefault(l => l.LevelId == userSession.CurrentLevelId)?
                                 .Modules.FirstOrDefault(m => m.ModuleId == userSession.CurrentModuleIdForNav)?
                                 .Lessons.FirstOrDefault(le => le.LessonId == userSession.CurrentLessonIdForContext);
            if (lesson != null)
            {
                var messageText = $"📖 *{lesson.Title}*\n\n{lesson.Content}";
                var inlineKeyboardMarkup = LessonKeyboard.GetLessonContentInlineKeyboard(userSession.CurrentLevelId, userSession.CurrentModuleIdForNav, lesson.LessonId);
                await _botClient.SendTextMessageAsync(chatId, messageText, parseMode: ParseMode.Markdown, replyMarkup: inlineKeyboardMarkup, cancellationToken: ct);
            } else { // Fallback
                await HandleShowLevelsAsync(chatId, userSession, cancellationToken);
            }
        }

        // --- Lesson Quiz Handling Methods ---
        public async Task HandleStartQuizSessionCallbackAsync(string callbackData, UserSession userSession, long chatId, CancellationToken ct)
        {
            var parts = callbackData.Split('_'); // Expected: quiz_{levelId}_{moduleId}_{lessonId}
            if (parts.Length < 4) { /* error */ return; }
             if (!int.TryParse(parts[1], out int levelId) ||
                !int.TryParse(parts[2], out int moduleId) ||
                !int.TryParse(parts[3], out int lessonId)) { /* error */ return; }

            Console.WriteLine($"[CBRouter] Start Quiz: Lvl:{levelId},Mod:{moduleId},Les:{lessonId}");
            var courseData = await GetCourseStructureAsync();
            var lesson = courseData?.Levels.FirstOrDefault(l => l.LevelId == levelId)?
                                 .Modules.FirstOrDefault(m => m.ModuleId == moduleId)?
                                 .Lessons.FirstOrDefault(le => le.LessonId == lessonId);

            if (lesson == null || lesson.Quiz == null || !lesson.Quiz.Any())
            {
                 await _botClient.AnswerCallbackQueryAsync(userSession.CurrentInteractionContext.Split('|')[0], "Для этого урока нет вопросов теста.", showAlert:true, cancellationToken: ct);
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
            var parts = callbackData.Split('_'); // Expected: quiz_answer_{levelId}_{moduleId}_{lessonId}_{questionIndex}_{optionIndex}
            if (parts.Length < 6) { /* error */ return; }
            // int levelId = int.Parse(parts[2]);
            // int moduleId = int.Parse(parts[3]);
            // int lessonId = int.Parse(parts[4]);
            if (!int.TryParse(parts[4], out int questionIndexFromCallback) || // Index in callback is from CurrentLessonQuizQuestionIndex
                !int.TryParse(parts[5], out int chosenOptionIndex)) { /* error */ return; }

            if (userSession.CurrentLessonQuizQuestions == null || questionIndexFromCallback != userSession.CurrentLessonQuizQuestionIndex)
            { /* error, question mismatch or quiz not active */ return; }

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
                await HandleShowLevelsAsync(chatId, userSession, cancellationToken);
            }
        }
    }
}
