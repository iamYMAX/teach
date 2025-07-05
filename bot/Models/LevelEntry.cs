using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OmnieyeBot.Models
{
    public class LevelEntry
    {
        [JsonPropertyName("levelId")]
        public int LevelId { get; set; } // В вашем JSON это int, если может быть string, нужно изменить

        [JsonPropertyName("title")]
        public string Title { get; set; }

        // Description для уровня отсутствует в вашем JSON, но может быть полезным. Пока не добавляю.
        // Если нужно, раскомментируйте:
        // [JsonPropertyName("description")]
        // public string Description { get; set; }

        [JsonPropertyName("modules")]
        public List<ModuleContent> Modules { get; set; } = new List<ModuleContent>();
    }
}
