using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OmnieyeBot.Models
{
    public class ModuleContent
    {
        [JsonPropertyName("moduleId")]
        public int ModuleId { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("lessons")]
        public List<LessonContent> Lessons { get; set; } = new List<LessonContent>();
    }
}
