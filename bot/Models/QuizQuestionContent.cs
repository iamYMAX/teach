using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OmnieyeBot.Models
{
    public class QuizQuestionContent
    {
        [JsonPropertyName("question")]
        public string Question { get; set; }

        [JsonPropertyName("options")]
        public List<string> Options { get; set; } = new List<string>();

        [JsonPropertyName("correctOptionIndex")]
        public int CorrectOptionIndex { get; set; }
    }
}
