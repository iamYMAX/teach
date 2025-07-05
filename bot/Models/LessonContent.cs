using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OmnieyeBot.Models
{
    public class LessonContent
    {
        [JsonPropertyName("lessonId")]
        public int LessonId { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }

        [JsonPropertyName("flashcards")]
        public List<FlashcardContent> Flashcards { get; set; } = new List<FlashcardContent>();

        [JsonPropertyName("quiz")]
        public List<QuizQuestionContent> Quiz { get; set; } = new List<QuizQuestionContent>();
    }
}
