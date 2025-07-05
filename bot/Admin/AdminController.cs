using Omnieye.Bot.CoreModels;
using Omnieye.Bot.Services; // Added for AdminActivityLogger
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using System.Threading.Tasks;

namespace Omnieye.Bot.Admin
{
    public class AdminController
    {
        private readonly ITelegramBotClient _botClient;
        private readonly AdminService _adminService;
        private readonly AdminActivityLogger _activityLogger; // Added

        public const long AdminTelegramId = 907191168; // Made public
        private static readonly Dictionary<long, (string CurrentMainOperation, string Step, string? TempDataJson)> _userStates = new();

        public AdminController(ITelegramBotClient botClient, AdminService adminService, AdminActivityLogger logger) // Modified
        {
            _botClient = botClient;
            _adminService = adminService;
            _activityLogger = logger; // Added
        }

        private void ClearUserState(long chatId) => _userStates.Remove(chatId);
        private void SetUserState(long chatId, string mainOp, string step, string? dataJson = null) => _userStates[chatId] = (mainOp, step, dataJson);
        private bool TryGetUserState(long chatId, out (string CurrentMainOperation, string Step, string? TempDataJson) state) => _userStates.TryGetValue(chatId, out state);

        public async Task HandleAdminCommandAsync(Message message)
        {
            if (message.From == null || message.From.Id != AdminTelegramId) { await _botClient.SendTextMessageAsync(message.Chat.Id, "Access denied."); return; }
            ClearUserState(message.Chat.Id);
            if (message.Text != null && message.Text.Equals("/admin", StringComparison.OrdinalIgnoreCase)) await SendAdminMainMenuAsync(message.Chat.Id);
        }

        public async Task HandleAdminTextMessageAsync(Message message)
        {
            if (message.From == null || message.From.Id != AdminTelegramId || string.IsNullOrWhiteSpace(message.Text)) return;
            long chatId = message.Chat.Id;

            if (TryGetUserState(chatId, out var state))
            {
                if (state.CurrentMainOperation == "level_add" && state.Step == "awaiting_name") { ClearUserState(chatId); await AddNewDifficultyLevelAsync(chatId, message.Text, message.MessageId); }
                else if (state.CurrentMainOperation == "level_rename" && state.Step == "awaiting_name" && state.TempDataJson != null) { ClearUserState(chatId); await RenameDifficultyLevelAsync(chatId, state.TempDataJson, message.Text, message.MessageId); }
                else if (state.CurrentMainOperation == "lesson_add" || state.CurrentMainOperation == "lesson_edit") await ProcessLessonTextMessageAsync(chatId, message.Text, message.MessageId, state);
                else if (state.CurrentMainOperation == "flashcard_add" || state.CurrentMainOperation == "flashcard_edit") await ProcessFlashcardTextMessageAsync(chatId, message.Text, message.MessageId, state);
                else if (state.CurrentMainOperation == "test_add") await ProcessTestTextMessageAsync(chatId, message.Text, message.MessageId, state);
                else if (state.CurrentMainOperation == "lesson_search" && state.Step == "awaiting_search_term")
                {
                    ClearUserState(chatId);
                    // Ideally, edit the message that prompted for search term. message.MessageId is the search term message itself.
                    // For now, send new message then show list in it.
                    var tempMsg = await _botClient.SendTextMessageAsync(chatId, $"Ищем уроки по запросу: \"{message.Text}\"...");
                    await ShowLessonsListAsync(chatId, tempMsg.MessageId, searchTerm: message.Text);
                }
                else if (state.CurrentMainOperation == "flashcard_search" && state.Step == "awaiting_search_term")
                {
                    ClearUserState(chatId);
                    var tempMsg = await _botClient.SendTextMessageAsync(chatId, $"Ищем карточки по запросу: \"{message.Text}\"...");
                    await ShowFlashcardsListAsync(chatId, tempMsg.MessageId, searchTerm: message.Text);
                }
                else if (state.CurrentMainOperation == "test_search" && state.Step == "awaiting_search_term")
                {
                    ClearUserState(chatId);
                    var tempMsg = await _botClient.SendTextMessageAsync(chatId, $"Ищем тесты по запросу: \"{message.Text}\"...");
                    await ShowTestsListAsync(chatId, tempMsg.MessageId, searchTerm: message.Text);
                }
            }
        }

        private async Task SendAdminMainMenuAsync(long chatId, int? messageId = null)
        {
            var kb = new InlineKeyboardMarkup(new[] {
                new [] { InlineKeyboardButton.WithCallbackData("Уроки 📚", "admin_lessons_menu"), InlineKeyboardButton.WithCallbackData("Тесты 📝", "admin_tests_menu") },
                new [] { InlineKeyboardButton.WithCallbackData("Флеш-карточки 🗂️", "admin_flashcards_menu"), InlineKeyboardButton.WithCallbackData("Уровни сложности 📊", "admin_levels_menu") }
            });
            await SendOrEditMessageAsync(chatId, messageId, "Админ-панель. Выберите раздел:", kb);
        }

        public async Task HandleCallbackQueryAsync(CallbackQuery cbq)
        {
            if (cbq.From.Id != AdminTelegramId || cbq.Message == null || cbq.Data == null) { if (cbq.Data != null) await _botClient.AnswerCallbackQueryAsync(cbq.Id, "Access denied/invalid."); return; }
            await _botClient.AnswerCallbackQueryAsync(cbq.Id);
            long chatId = cbq.Message.Chat.Id; int msgId = cbq.Message.MessageId; string cbData = cbq.Data;

            if (cbData == "admin_main_menu" || cbData.EndsWith("_menu") || cbData.EndsWith("_list")) ClearUserState(chatId);

            if (cbData == "admin_main_menu") await SendAdminMainMenuAsync(chatId, msgId);
            else if (cbData.StartsWith("admin_levels_")) await HandleLevelsCallbackAsync(chatId, msgId, cbData);
            else if (cbData.StartsWith("admin_lessons_")) await HandleLessonsCallbackAsync(chatId, msgId, cbData);
            else if (cbData.StartsWith("admin_flashcards_")) await HandleFlashcardsCallbackAsync(chatId, msgId, cbData);
            else if (cbData.StartsWith("admin_tests_")) await HandleTestsCallbackAsync(chatId, msgId, cbData);
        }

        #region Difficulty Levels Management
        private async Task HandleLevelsCallbackAsync(long chatId, int messageId, string cbData) {
            if (cbData == "admin_levels_menu") {
                var kb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить уровень", "admin_levels_add_prompt") }, new[] { InlineKeyboardButton.WithCallbackData("📋 Список уровней", "admin_levels_list") }, GetBackToMenuRow("admin_main_menu") });
                await SendOrEditMessageAsync(chatId, messageId, "Управление уровнями сложности:", kb);
            } else if (cbData == "admin_levels_add_prompt") {
                SetUserState(chatId, "level_add", "awaiting_name"); await SendOrEditMessageAsync(chatId, messageId, "Введите название нового уровня:", GetBackToMenuMarkup("admin_levels_menu"));
            } else if (cbData == "admin_levels_list") await ShowDifficultyLevelsListAsync(chatId, messageId);
            else if (cbData.StartsWith("admin_levels_rename_prompt_")) {
                var levelId = cbData.Substring("admin_levels_rename_prompt_".Length); var level = await _adminService.GetDifficultyLevelByIdAsync(levelId);
                if (level == null) { await NotifyActionOutcome(chatId, "Уровень не найден."); await ShowDifficultyLevelsListAsync(chatId, messageId); return; }
                SetUserState(chatId, "level_rename", "awaiting_name", levelId); await SendOrEditMessageAsync(chatId, messageId, $"Новое название для '{level.Name}':", GetBackToMenuMarkup("admin_levels_list"));
            } else if (cbData.StartsWith("admin_levels_delete_confirm_")) {
                var levelId = cbData.Substring("admin_levels_delete_confirm_".Length); var level = await _adminService.GetDifficultyLevelByIdAsync(levelId);
                if (level == null) { await NotifyActionOutcome(chatId, "Уровень не найден."); await ShowDifficultyLevelsListAsync(chatId, messageId); return; }
                var confirmKb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("✅ Удалить", $"admin_levels_delete_execute_{levelId}"), InlineKeyboardButton.WithCallbackData("❌ Отмена", "admin_levels_list") }});
                await SendOrEditMessageAsync(chatId, messageId, $"Удалить уровень '{level.Name}'?", confirmKb);
            } else if (cbData.StartsWith("admin_levels_delete_execute_")) {
                var levelId = cbData.Substring("admin_levels_delete_execute_".Length); var level = await _adminService.GetDifficultyLevelByIdAsync(levelId);
                bool success = await _adminService.DeleteDifficultyLevelAsync(levelId);
                if (success) await _activityLogger.LogAsync(AdminTelegramId, "Level Deleted", $"ID: {levelId}, Name: {level?.Name}");
                await NotifyActionOutcome(chatId, success ? $"Уровень '{level?.Name}' удален." : "Ошибка удаления."); await ShowDifficultyLevelsListAsync(chatId, messageId);
            }
        }
        private async Task AddNewDifficultyLevelAsync(long chatId, string name, int promptMsgId) {
            if (string.IsNullOrWhiteSpace(name)) { await SendOrEditMessageAsync(chatId, promptMsgId, "Название не может быть пустым. Введите название:", GetBackToMenuMarkup("admin_levels_menu")); SetUserState(chatId, "level_add", "awaiting_name"); return; }
            try {
                var newLevel = new DifficultyLevel { Name = name };
                await _adminService.AddDifficultyLevelAsync(newLevel);
                await _activityLogger.LogAsync(AdminTelegramId, "Level Added", $"ID: {newLevel.Id}, Name: {name}");
                await SendOrEditMessageAsync(chatId, promptMsgId, $"Уровень '{name}' добавлен.", GetBackToMenuMarkup("admin_levels_menu"));
            }
            catch (Exception ex) { await SendOrEditMessageAsync(chatId, promptMsgId, $"Ошибка: {ex.Message}. Введите название:", GetBackToMenuMarkup("admin_levels_menu")); SetUserState(chatId, "level_add", "awaiting_name"); }
        }
        private async Task RenameDifficultyLevelAsync(long chatId, string levelId, string newName, int promptMsgId) {
            if (string.IsNullOrWhiteSpace(newName)) { await SendOrEditMessageAsync(chatId, promptMsgId, "Название не может быть пустым. Введите новое название:", GetBackToMenuMarkup("admin_levels_list")); SetUserState(chatId, "level_rename", "awaiting_name", levelId); return; }
            try {
                var oldLevel = await _adminService.GetDifficultyLevelByIdAsync(levelId);
                bool success = await _adminService.RenameDifficultyLevelAsync(levelId, newName);
                if(success) await _activityLogger.LogAsync(AdminTelegramId, "Level Renamed", $"ID: {levelId}, Old Name: {oldLevel?.Name}, New Name: {newName}");
                await NotifyActionOutcome(chatId, $"Уровень '{oldLevel?.Name}' переименован в '{newName}'.");
                await ShowDifficultyLevelsListAsync(chatId, promptMsgId);
            }
            catch (Exception ex) { await NotifyActionOutcome(chatId, $"Ошибка: {ex.Message}."); SetUserState(chatId, "level_rename", "awaiting_name", levelId); await SendOrEditMessageAsync(chatId, promptMsgId, $"Ошибка: {ex.Message}. Введите новое название:", GetBackToMenuMarkup("admin_levels_list"));}
        }
        private async Task ShowDifficultyLevelsListAsync(long chatId, int messageId) {
            var levels = (await _adminService.GetDifficultyLevelsAsync()).OrderBy(l => l.Name).ToList(); var text = !levels.Any() ? "Список уровней пуст." : "Уровни сложности:";
            var rows = levels.Select(l => new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData($"✏️ {l.Name}", $"admin_levels_rename_prompt_{l.Id}"), InlineKeyboardButton.WithCallbackData("🗑️", $"admin_levels_delete_confirm_{l.Id}") }).ToList();
            rows.Add(GetBackToMenuRow("admin_levels_menu")); await SendOrEditMessageAsync(chatId, messageId, text, new InlineKeyboardMarkup(rows));
        }
        #endregion

        #region Lessons Management
        private async Task HandleLessonsCallbackAsync(long chatId, int messageId, string cbData) {
            if (cbData == "admin_lessons_menu") {
                var kb = new InlineKeyboardMarkup(new[] {
                    new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить урок", "admin_lessons_add_start") },
                    new[] { InlineKeyboardButton.WithCallbackData("📋 Список уроков", "admin_lessons_list") },
                    new[] { InlineKeyboardButton.WithCallbackData("🔍 Найти урок", "admin_lessons_search_prompt") }, // Added Search
                    GetBackToMenuRow("admin_main_menu")
                });
                await SendOrEditMessageAsync(chatId, messageId, "Управление уроками:", kb);
            } else if (cbData == "admin_lessons_search_prompt") {
                SetUserState(chatId, "lesson_search", "awaiting_search_term");
                await SendOrEditMessageAsync(chatId, messageId, "Введите название урока для поиска:", GetBackToMenuMarkup("admin_lessons_menu"));
            }
            else if (cbData == "admin_lessons_add_start") {
                SetUserState(chatId, "lesson_add", "awaiting_name", JsonSerializer.Serialize(new Lesson())); await SendOrEditMessageAsync(chatId, messageId, "Введите название нового урока:", GetBackToMenuMarkup("admin_lessons_menu"));
            } else if (cbData.StartsWith("admin_lessons_set_level_")) {
                var parts = cbData.Substring("admin_lessons_set_level_".Length).Split(new[]{"_lesson_"}, StringSplitOptions.None); var levelId = parts[0]; var lessonId = parts[1];
                if (TryGetUserState(chatId, out var state) && (state.CurrentMainOperation == "lesson_add" || state.CurrentMainOperation == "lesson_edit")) {
                    Lesson lesson = JsonSerializer.Deserialize<Lesson>(state.TempDataJson ?? "{}") ?? new Lesson(); lesson.DifficultyLevelId = levelId;
                    if (state.CurrentMainOperation == "lesson_add") {
                        await _adminService.AddLessonAsync(lesson);
                        await _activityLogger.LogAsync(AdminTelegramId, "Lesson Added", $"ID: {lesson.Id}, Name: {lesson.Name}, LevelID: {lesson.DifficultyLevelId}");
                        await SendOrEditMessageAsync(chatId, messageId, $"Урок '{lesson.Name}' добавлен.", GetBackToMenuMarkup("admin_lessons_menu"));
                    } else {
                        await _adminService.UpdateLessonAsync(lesson);
                        await _activityLogger.LogAsync(AdminTelegramId, "Lesson Updated", $"ID: {lesson.Id}, Name: {lesson.Name}, Desc: {Truncate(lesson.Description,20)}, LevelID: {lesson.DifficultyLevelId}");
                        await SendOrEditMessageAsync(chatId, messageId, $"Урок '{lesson.Name}' обновлен.", GetBackToMenuMarkup("admin_lessons_list"));
                    }
                    ClearUserState(chatId);
                }
            } else if (cbData == "admin_lessons_list") await ShowLessonsListAsync(chatId, messageId);
            else if (cbData.StartsWith("admin_lessons_edit_start_")) {
                var lessonId = cbData.Substring("admin_lessons_edit_start_".Length); var lesson = await _adminService.GetLessonByIdAsync(lessonId);
                if (lesson == null) { await NotifyActionOutcome(chatId, "Урок не найден."); await ShowLessonsListAsync(chatId, messageId); return; }
                SetUserState(chatId, "lesson_edit", "awaiting_name", JsonSerializer.Serialize(lesson)); await SendOrEditMessageAsync(chatId, messageId, $"Редактирование урока '{lesson.Name}'.\nВведите новое название (или отправьте '.', чтобы оставить '{lesson.Name}'):", GetBackToMenuMarkup("admin_lessons_list"));
            } else if (cbData.StartsWith("admin_lessons_delete_confirm_")) {
                var lessonId = cbData.Substring("admin_lessons_delete_confirm_".Length); var lesson = await _adminService.GetLessonByIdAsync(lessonId);
                if (lesson == null) { await NotifyActionOutcome(chatId, "Урок не найден."); await ShowLessonsListAsync(chatId, messageId); return; }
                var confirmKb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("✅ Удалить", $"admin_lessons_delete_execute_{lessonId}"), InlineKeyboardButton.WithCallbackData("❌ Отмена", "admin_lessons_list") }});
                await SendOrEditMessageAsync(chatId, messageId, $"Удалить урок '{lesson.Name}'?", confirmKb);
            } else if (cbData.StartsWith("admin_lessons_delete_execute_")) {
                var lessonId = cbData.Substring("admin_lessons_delete_execute_".Length); var lesson = await _adminService.GetLessonByIdAsync(lessonId);
                bool success = await _adminService.DeleteLessonAsync(lessonId);
                if(success && lesson != null) await _activityLogger.LogAsync(AdminTelegramId, "Lesson Deleted", $"ID: {lessonId}, Name: {lesson.Name}");
                await NotifyActionOutcome(chatId, success ? $"Урок '{lesson?.Name}' удален." : "Ошибка удаления урока.");
                await ShowLessonsListAsync(chatId, messageId);
            }
        }
        private async Task ProcessLessonTextMessageAsync(long chatId, string text, int messageId, (string CurrentMainOperation, string Step, string? TempDataJson) state) {
            Lesson lesson = JsonSerializer.Deserialize<Lesson>(state.TempDataJson ?? "{}") ?? new Lesson();
            string nextStep = "", promptText = ""; bool reprompt = false;
            if (state.Step == "awaiting_name") {
                if (string.IsNullOrWhiteSpace(text) || (text == "." && state.CurrentMainOperation == "lesson_add")) { promptText = "Название не может быть пустым. Введите название:"; reprompt = true; }
                else { if (text != ".") lesson.Name = text; nextStep = "awaiting_description"; promptText = $"Название: {lesson.Name}\nВведите описание (или '.', чтобы оставить '{Truncate(lesson.Description,50)}'):"; }
            } else if (state.Step == "awaiting_description") {
                if (string.IsNullOrWhiteSpace(text) || (text == "." && state.CurrentMainOperation == "lesson_add" && string.IsNullOrEmpty(lesson.Description) )) { promptText = "Описание не может быть пустым. Введите описание:"; reprompt = true; }
                else { if (text != ".") lesson.Description = text; nextStep = "awaiting_level_id"; promptText = "Выберите уровень:"; }
            }

            if (reprompt) { await SendOrEditMessageAsync(chatId, messageId, promptText, GetBackToMenuMarkup(state.CurrentMainOperation == "lesson_add" ? "admin_lessons_menu" : "admin_lessons_list")); SetUserState(chatId, state.CurrentMainOperation, state.Step, JsonSerializer.Serialize(lesson)); }
            else if (!string.IsNullOrEmpty(nextStep)) {
                SetUserState(chatId, state.CurrentMainOperation, nextStep, JsonSerializer.Serialize(lesson));
                if (nextStep == "awaiting_level_id") await ShowLevelSelectionForLessonAsync(chatId, messageId, lesson, state.CurrentMainOperation);
                else await SendOrEditMessageAsync(chatId, messageId, promptText, GetBackToMenuMarkup(state.CurrentMainOperation == "lesson_add" ? "admin_lessons_menu" : "admin_lessons_list"));
            }
        }
        private async Task ShowLevelSelectionForLessonAsync(long chatId, int messageId, Lesson lessonInProgress, string operationType) {
            var levels = await _adminService.GetDifficultyLevelsAsync();
            if (!levels.Any()) { ClearUserState(chatId); await SendOrEditMessageAsync(chatId, messageId, "Нет уровней. Добавьте их сначала.", GetBackToMenuMarkup("admin_levels_menu")); return; }
            var rows = levels.OrderBy(l => l.Name).Select(l => new List<InlineKeyboardButton>{ InlineKeyboardButton.WithCallbackData(l.Name, $"admin_lessons_set_level_{l.Id}_lesson_{lessonInProgress.Id}")}).ToList();
            rows.Add(GetBackToMenuRow(operationType == "lesson_add" ? "admin_lessons_menu" : "admin_lessons_list"));
            await SendOrEditMessageAsync(chatId, messageId, $"Урок: {lessonInProgress.Name}\nОписание: {Truncate(lessonInProgress.Description,100)}\nВыберите уровень:", new InlineKeyboardMarkup(rows));
        }
        private async Task ShowLessonsListAsync(long chatId, int messageId, string? searchTerm = null)
        {
            var allLessons = await _adminService.GetLessonsAsync();
            List<Lesson> lessonsToShow;
            string text;

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                lessonsToShow = allLessons.Where(l => l.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)).OrderBy(l => l.Name).ToList();
                text = lessonsToShow.Any() ? $"Результаты поиска по \"{searchTerm}\":" : $"Уроки по запросу \"{searchTerm}\" не найдены.";
            }
            else
            {
                lessonsToShow = allLessons.OrderBy(l => l.Name).ToList();
                text = !lessonsToShow.Any() ? "Список уроков пуст." : "Уроки:";
            }

            var rows = new List<List<InlineKeyboardButton>>();
            if (lessonsToShow.Any()) {
                foreach (var lesson in lessonsToShow) {
                    var levelName = "";
                    if (!string.IsNullOrEmpty(lesson.DifficultyLevelId)) {
                        var level = await _adminService.GetDifficultyLevelByIdAsync(lesson.DifficultyLevelId);
                        if (level != null) levelName = $" ({level.Name})";
                    }
                    rows.Add(new List<InlineKeyboardButton> {
                        InlineKeyboardButton.WithCallbackData($"✏️ {lesson.Name}{levelName}", $"admin_lessons_edit_start_{lesson.Id}"),
                        InlineKeyboardButton.WithCallbackData("🗑️", $"admin_lessons_delete_confirm_{lesson.Id}")
                    });
                }
            }

            if (!string.IsNullOrWhiteSpace(searchTerm)) // If it was a search, add "Show All"
            {
                rows.Add(new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData("📋 Показать все уроки", "admin_lessons_list") });
            }
            rows.Add(GetBackToMenuRow("admin_lessons_menu"));
            await SendOrEditMessageAsync(chatId, messageId, text, new InlineKeyboardMarkup(rows));
        }
        #endregion

        #region Flashcards Management
        private async Task HandleFlashcardsCallbackAsync(long chatId, int messageId, string cbData) {
            if (cbData == "admin_flashcards_menu") {
                var kb = new InlineKeyboardMarkup(new[] {
                    new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить карточку", "admin_flashcards_add_start") },
                    new[] { InlineKeyboardButton.WithCallbackData("📋 Список карточек", "admin_flashcards_list") },
                    new[] { InlineKeyboardButton.WithCallbackData("🔍 Найти карточку", "admin_flashcards_search_prompt") }, // Added Search
                    GetBackToMenuRow("admin_main_menu")
                });
                await SendOrEditMessageAsync(chatId, messageId, "Управление флеш-карточками:", kb);
            } else if (cbData == "admin_flashcards_search_prompt") {
                SetUserState(chatId, "flashcard_search", "awaiting_search_term");
                await SendOrEditMessageAsync(chatId, messageId, "Введите вопрос карточки для поиска:", GetBackToMenuMarkup("admin_flashcards_menu"));
            }
            else if (cbData == "admin_flashcards_add_start") {
                SetUserState(chatId, "flashcard_add", "awaiting_question", JsonSerializer.Serialize(new Flashcard())); await SendOrEditMessageAsync(chatId, messageId, "Введите вопрос для новой флеш-карточки:", GetBackToMenuMarkup("admin_flashcards_menu"));
            } else if (cbData.StartsWith("admin_flashcards_set_level_")) {
                var parts = cbData.Substring("admin_flashcards_set_level_".Length).Split(new[]{"_flashcard_"}, StringSplitOptions.None); var levelId = parts[0]; var flashcardId = parts[1];
                if (TryGetUserState(chatId, out var state) && (state.CurrentMainOperation == "flashcard_add" || state.CurrentMainOperation == "flashcard_edit")) {
                    Flashcard flashcard = JsonSerializer.Deserialize<Flashcard>(state.TempDataJson ?? "{}") ?? new Flashcard(); flashcard.DifficultyLevelId = levelId;
                    if (state.CurrentMainOperation == "flashcard_add") {
                        await _adminService.AddFlashcardAsync(flashcard);
                        await _activityLogger.LogAsync(AdminTelegramId, "Flashcard Added", $"ID: {flashcard.Id}, Q: {Truncate(flashcard.Question, 30)}, LvlID: {levelId}");
                        await SendOrEditMessageAsync(chatId, messageId, $"Карточка '{Truncate(flashcard.Question, 20)}' добавлена.", GetBackToMenuMarkup("admin_flashcards_menu"));
                    } else {
                        await _adminService.UpdateFlashcardAsync(flashcard);
                        await _activityLogger.LogAsync(AdminTelegramId, "Flashcard Updated", $"ID: {flashcard.Id}, Q: {Truncate(flashcard.Question,30)}, A: {Truncate(flashcard.Answer,30)}, LvlID: {levelId}");
                        await SendOrEditMessageAsync(chatId, messageId, $"Карточка '{Truncate(flashcard.Question, 20)}' обновлена.", GetBackToMenuMarkup("admin_flashcards_list"));
                    }
                    ClearUserState(chatId);
                }
            } else if (cbData == "admin_flashcards_list") await ShowFlashcardsListAsync(chatId, messageId);
            else if (cbData.StartsWith("admin_flashcards_edit_start_")) {
                var flashcardId = cbData.Substring("admin_flashcards_edit_start_".Length); var flashcard = await _adminService.GetFlashcardByIdAsync(flashcardId);
                if (flashcard == null) { await NotifyActionOutcome(chatId, "Карточка не найдена."); await ShowFlashcardsListAsync(chatId, messageId); return; }
                SetUserState(chatId, "flashcard_edit", "awaiting_question", JsonSerializer.Serialize(flashcard)); await SendOrEditMessageAsync(chatId, messageId, $"Редактирование карточки.\nВопрос: {flashcard.Question}\nВведите новый вопрос (или '.', чтобы оставить):", GetBackToMenuMarkup("admin_flashcards_list"));
            } else if (cbData.StartsWith("admin_flashcards_delete_confirm_")) {
                var flashcardId = cbData.Substring("admin_flashcards_delete_confirm_".Length); var flashcard = await _adminService.GetFlashcardByIdAsync(flashcardId);
                if (flashcard == null) { await NotifyActionOutcome(chatId, "Карточка не найдена."); await ShowFlashcardsListAsync(chatId, messageId); return; }
                var confirmKb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("✅ Удалить", $"admin_flashcards_delete_execute_{flashcardId}"), InlineKeyboardButton.WithCallbackData("❌ Отмена", "admin_flashcards_list") }});
                await SendOrEditMessageAsync(chatId, messageId, $"Удалить карточку '{Truncate(flashcard.Question, 30)}'?", confirmKb);
            } else if (cbData.StartsWith("admin_flashcards_delete_execute_")) {
                var flashcardId = cbData.Substring("admin_flashcards_delete_execute_".Length); var flashcard = await _adminService.GetFlashcardByIdAsync(flashcardId);
                bool success = await _adminService.DeleteFlashcardAsync(flashcardId);
                if(success && flashcard != null) await _activityLogger.LogAsync(AdminTelegramId, "Flashcard Deleted", $"ID: {flashcardId}, Q: {Truncate(flashcard.Question,30)}");
                await NotifyActionOutcome(chatId, success ? $"Карточка '{Truncate(flashcard?.Question, 30)}' удалена." : "Ошибка удаления.");
                await ShowFlashcardsListAsync(chatId, messageId);
            }
        }
        private async Task ProcessFlashcardTextMessageAsync(long chatId, string text, int messageId, (string CurrentMainOperation, string Step, string? TempDataJson) state) {
            Flashcard flashcard = JsonSerializer.Deserialize<Flashcard>(state.TempDataJson ?? "{}") ?? new Flashcard();
            string nextStep = "", promptText = ""; bool reprompt = false;
            if (state.Step == "awaiting_question") {
                if (string.IsNullOrWhiteSpace(text) || (text == "." && state.CurrentMainOperation == "flashcard_add")) { promptText = "Вопрос не может быть пустым. Введите вопрос:"; reprompt = true; }
                else { if (text != ".") flashcard.Question = text; nextStep = "awaiting_answer"; promptText = $"Вопрос: {Truncate(flashcard.Question, 50)}\nВведите ответ (или '.', чтобы оставить '{Truncate(flashcard.Answer, 50)}'):"; }
            } else if (state.Step == "awaiting_answer") {
                if (string.IsNullOrWhiteSpace(text) || (text == "." && state.CurrentMainOperation == "flashcard_add" && string.IsNullOrEmpty(flashcard.Answer) )) { promptText = "Ответ не может быть пустым. Введите ответ:"; reprompt = true; }
                else { if (text != ".") flashcard.Answer = text; nextStep = "awaiting_level_id"; promptText = "Выберите уровень:"; }
            }

            if (reprompt) { await SendOrEditMessageAsync(chatId, messageId, promptText, GetBackToMenuMarkup(state.CurrentMainOperation == "flashcard_add" ? "admin_flashcards_menu" : "admin_flashcards_list")); SetUserState(chatId, state.CurrentMainOperation, state.Step, JsonSerializer.Serialize(flashcard)); }
            else if (!string.IsNullOrEmpty(nextStep)) {
                SetUserState(chatId, state.CurrentMainOperation, nextStep, JsonSerializer.Serialize(flashcard));
                if (nextStep == "awaiting_level_id") await ShowLevelSelectionForFlashcardAsync(chatId, messageId, flashcard, state.CurrentMainOperation);
                else await SendOrEditMessageAsync(chatId, messageId, promptText, GetBackToMenuMarkup(state.CurrentMainOperation == "flashcard_add" ? "admin_flashcards_menu" : "admin_flashcards_list"));
            }
        }
        private async Task ShowLevelSelectionForFlashcardAsync(long chatId, int messageId, Flashcard flashcardInProgress, string operationType) {
            var levels = await _adminService.GetDifficultyLevelsAsync();
            if (!levels.Any()) { ClearUserState(chatId); await SendOrEditMessageAsync(chatId, messageId, "Нет уровней. Добавьте их сначала.", GetBackToMenuMarkup("admin_levels_menu")); return; }
            var rows = levels.OrderBy(l => l.Name).Select(l => new List<InlineKeyboardButton>{ InlineKeyboardButton.WithCallbackData(l.Name, $"admin_flashcards_set_level_{l.Id}_flashcard_{flashcardInProgress.Id}")}).ToList();
            rows.Add(GetBackToMenuRow(operationType == "flashcard_add" ? "admin_flashcards_menu" : "admin_flashcards_list"));
            await SendOrEditMessageAsync(chatId, messageId, $"Карточка: {Truncate(flashcardInProgress.Question,30)} / {Truncate(flashcardInProgress.Answer,30)}\nВыберите уровень:", new InlineKeyboardMarkup(rows));
        }
        private async Task ShowFlashcardsListAsync(long chatId, int messageId, string? searchTerm = null)
        {
            var allFlashcards = await _adminService.GetFlashcardsAsync();
            List<Flashcard> flashcardsToShow;
            string text;

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                flashcardsToShow = allFlashcards.Where(fc =>
                    (fc.Question != null && fc.Question.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) ||
                    (fc.Answer != null && fc.Answer.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)) // Optional: search in answers too
                ).OrderBy(fc => fc.Question).ToList();
                text = flashcardsToShow.Any() ? $"Результаты поиска по \"{searchTerm}\":" : $"Карточки по запросу \"{searchTerm}\" не найдены.";
            }
            else
            {
                flashcardsToShow = allFlashcards.OrderBy(fc => fc.Question).ToList();
                text = !flashcardsToShow.Any() ? "Список флеш-карточек пуст." : "Флеш-карточки:";
            }

            var rows = new List<List<InlineKeyboardButton>>();
            if (flashcardsToShow.Any()) {
                foreach (var fc in flashcardsToShow) {
                    var levelName = ""; if (!string.IsNullOrEmpty(fc.DifficultyLevelId)) { var level = await _adminService.GetDifficultyLevelByIdAsync(fc.DifficultyLevelId); if (level != null) levelName = $" ({level.Name})"; }
                    rows.Add(new List<InlineKeyboardButton> {
                        InlineKeyboardButton.WithCallbackData($"✏️ {Truncate(fc.Question, 20)}{levelName}", $"admin_flashcards_edit_start_{fc.Id}"),
                        InlineKeyboardButton.WithCallbackData("🗑️", $"admin_flashcards_delete_confirm_{fc.Id}")
                    });
                }
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                rows.Add(new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData("📋 Показать все карточки", "admin_flashcards_list") });
            }
            rows.Add(GetBackToMenuRow("admin_flashcards_menu"));
            await SendOrEditMessageAsync(chatId, messageId, text, new InlineKeyboardMarkup(rows));
        }
        #endregion

        #region Tests Management (Simplified)
        private async Task HandleTestsCallbackAsync(long chatId, int messageId, string cbData)
        {
            if (cbData == "admin_tests_menu") {
                var kb = new InlineKeyboardMarkup(new[] {
                    new[] { InlineKeyboardButton.WithCallbackData("➕ Добавить тест (название, уровень)", "admin_tests_add_start") },
                    new[] { InlineKeyboardButton.WithCallbackData("📋 Список тестов", "admin_tests_list") },
                    new[] { InlineKeyboardButton.WithCallbackData("🔍 Найти тест", "admin_tests_search_prompt") }, // Added Search
                    GetBackToMenuRow("admin_main_menu")
                });
                await SendOrEditMessageAsync(chatId, messageId, "Управление тестами (упрощенное):", kb);
            } else if (cbData == "admin_tests_search_prompt") {
                SetUserState(chatId, "test_search", "awaiting_search_term");
                await SendOrEditMessageAsync(chatId, messageId, "Введите название теста для поиска:", GetBackToMenuMarkup("admin_tests_menu"));
            }
            else if (cbData == "admin_tests_add_start") {
                SetUserState(chatId, "test_add", "awaiting_test_name", JsonSerializer.Serialize(new Test()));
                await SendOrEditMessageAsync(chatId, messageId, "Введите название нового теста:", GetBackToMenuMarkup("admin_tests_menu"));
            } else if (cbData.StartsWith("admin_tests_set_level_")) {
                var parts = cbData.Substring("admin_tests_set_level_".Length).Split(new[]{"_test_"}, StringSplitOptions.None); var levelId = parts[0]; var testId = parts[1];
                if (TryGetUserState(chatId, out var state) && state.CurrentMainOperation == "test_add") {
                    Test test = JsonSerializer.Deserialize<Test>(state.TempDataJson ?? "{}") ?? new Test(); test.DifficultyLevelId = levelId;
                    try {
                        await _adminService.AddTestAsync(test);
                        await _activityLogger.LogAsync(AdminTelegramId, "Test Added (Simplified)", $"ID: {test.Id}, Name: {test.TestName}, LevelID: {test.DifficultyLevelId}");
                        await SendOrEditMessageAsync(chatId, messageId, $"Тест '{test.TestName}' добавлен (без вопросов).", GetBackToMenuMarkup("admin_tests_menu"));
                    } catch (InvalidOperationException ex) {
                         await SendOrEditMessageAsync(chatId, messageId, $"Ошибка: {ex.Message}. Тест не добавлен.", GetBackToMenuMarkup("admin_tests_menu"));
                    }
                    ClearUserState(chatId);
                }
            } else if (cbData == "admin_tests_list") await ShowTestsListAsync(chatId, messageId);
            else if (cbData.StartsWith("admin_tests_delete_confirm_")) {
                var testId = cbData.Substring("admin_tests_delete_confirm_".Length); var test = await _adminService.GetTestByIdAsync(testId);
                if (test == null) { await NotifyActionOutcome(chatId, "Тест не найден."); await ShowTestsListAsync(chatId, messageId); return; }
                var confirmKb = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("✅ Удалить", $"admin_tests_delete_execute_{testId}"), InlineKeyboardButton.WithCallbackData("❌ Отмена", "admin_tests_list") }});
                await SendOrEditMessageAsync(chatId, messageId, $"Удалить тест '{test.TestName}'?", confirmKb);
            } else if (cbData.StartsWith("admin_tests_delete_execute_")) {
                var testId = cbData.Substring("admin_tests_delete_execute_".Length); var test = await _adminService.GetTestByIdAsync(testId);
                bool success = await _adminService.DeleteTestAsync(testId);
                if(success && test != null) await _activityLogger.LogAsync(AdminTelegramId, "Test Deleted", $"ID: {testId}, Name: {test.TestName}");
                await NotifyActionOutcome(chatId, success ? $"Тест '{test?.TestName}' удален." : "Ошибка удаления.");
                await ShowTestsListAsync(chatId, messageId);
            }
        }

        private async Task ProcessTestTextMessageAsync(long chatId, string text, int messageId, (string CurrentMainOperation, string Step, string? TempDataJson) state)
        {
            Test test = JsonSerializer.Deserialize<Test>(state.TempDataJson ?? "{}") ?? new Test();
            if (state.Step == "awaiting_test_name") {
                if (string.IsNullOrWhiteSpace(text)) {
                    await SendOrEditMessageAsync(chatId, messageId, "Название теста не может быть пустым. Введите название:", GetBackToMenuMarkup("admin_tests_menu"));
                    SetUserState(chatId, state.CurrentMainOperation, state.Step, JsonSerializer.Serialize(test));
                    return;
                }
                test.TestName = text;
                SetUserState(chatId, state.CurrentMainOperation, "awaiting_test_level_id", JsonSerializer.Serialize(test));
                await ShowLevelSelectionForTestAsync(chatId, messageId, test, state.CurrentMainOperation);
            }
        }

        private async Task ShowLevelSelectionForTestAsync(long chatId, int messageId, Test testInProgress, string operationType)
        {
            var levels = await _adminService.GetDifficultyLevelsAsync();
            if (!levels.Any()) { ClearUserState(chatId); await SendOrEditMessageAsync(chatId, messageId, "Нет уровней. Добавьте их сначала.", GetBackToMenuMarkup("admin_levels_menu")); return; }
            var rows = levels.OrderBy(l => l.Name).Select(l => new List<InlineKeyboardButton>{ InlineKeyboardButton.WithCallbackData(l.Name, $"admin_tests_set_level_{l.Id}_test_{testInProgress.Id}")}).ToList();
            rows.Add(GetBackToMenuRow(operationType == "test_add" ? "admin_tests_menu" : "admin_tests_list"));
            await SendOrEditMessageAsync(chatId, messageId, $"Тест: {testInProgress.TestName}\nВыберите уровень:", new InlineKeyboardMarkup(rows));
        }

        private async Task ShowTestsListAsync(long chatId, int messageId, string? searchTerm = null)
        {
            var allTests = await _adminService.GetTestsAsync();
            List<Test> testsToShow;
            string text;

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                testsToShow = allTests.Where(t => t.TestName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.TestName).ToList();
                text = testsToShow.Any() ? $"Результаты поиска по \"{searchTerm}\":" : $"Тесты по запросу \"{searchTerm}\" не найдены.";
            }
            else
            {
                testsToShow = allTests.OrderBy(t => t.TestName).ToList();
                text = !testsToShow.Any() ? "Список тестов пуст." : "Тесты (упрощенный вид):";
            }

            var rows = new List<List<InlineKeyboardButton>>();
            if (testsToShow.Any()) {
                foreach (var test in testsToShow) {
                    var levelName = ""; if (!string.IsNullOrEmpty(test.DifficultyLevelId)) { var level = await _adminService.GetDifficultyLevelByIdAsync(test.DifficultyLevelId); if (level != null) levelName = $" ({level.Name})"; }
                    rows.Add(new List<InlineKeyboardButton> {
                        InlineKeyboardButton.WithCallbackData($"{Truncate(test.TestName, 25)}{levelName} (Вопросов: {test.Questions.Count})", $"admin_tests_view_{test.Id}"),
                        InlineKeyboardButton.WithCallbackData("🗑️", $"admin_tests_delete_confirm_{test.Id}")
                    });
                }
            }

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                rows.Add(new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData("📋 Показать все тесты", "admin_tests_list") });
            }
            rows.Add(GetBackToMenuRow("admin_tests_menu"));
            await SendOrEditMessageAsync(chatId, messageId, text, new InlineKeyboardMarkup(rows));
        }
        #endregion

        #region Helper Methods
        private async Task SendOrEditMessageAsync(long chatId, int? messageId, string text, IReplyMarkup? replyMarkup = null) {
            try {
                if (messageId.HasValue) await _botClient.EditMessageTextAsync(chatId, messageId.Value, text, replyMarkup: replyMarkup, parseMode: ParseMode.Html);
                else await _botClient.SendTextMessageAsync(chatId, text, replyMarkup: replyMarkup, parseMode: ParseMode.Html);
            } catch (Exception ex) { Console.WriteLine($"SendOrEditMsgErr: {ex.Message}. Fallback Send."); await _botClient.SendTextMessageAsync(chatId, text, replyMarkup: replyMarkup, parseMode: ParseMode.Html); }
        }
        private InlineKeyboardMarkup GetBackToMenuMarkup(string targetCb) => new InlineKeyboardMarkup(GetBackToMenuRow(targetCb));
        private List<InlineKeyboardButton> GetBackToMenuRow(string targetCb) => new List<InlineKeyboardButton> { InlineKeyboardButton.WithCallbackData("⬅️ Назад", targetCb) };
        private async Task NotifyActionOutcome(long chatId, string notificationText) { if (!string.IsNullOrWhiteSpace(notificationText)) await _botClient.SendTextMessageAsync(chatId, notificationText, parseMode: ParseMode.Html); }
        private string Truncate(string? value, int maxLength) { if (string.IsNullOrEmpty(value)) return ""; return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "..."; }

        public bool IsAdminAwaitingTextInput(long adminId)
        {
            // Check if the provided adminId matches the configured AdminTelegramId
            // and if there's any state associated with this ID.
            return adminId == AdminTelegramId && _userStates.ContainsKey(adminId);
        }
        #endregion
    }
}
