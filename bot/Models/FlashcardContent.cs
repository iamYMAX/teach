using System.Text.Json.Serialization;

namespace OmnieyeBot.Models
{
    public class FlashcardContent
    {
        [JsonPropertyName("question")]
        public string Question { get; set; }

        [JsonPropertyName("answer")]
        public string Answer { get; set; }
    }
}
