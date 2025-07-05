using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OmnieyeBot.Models
{
    public class CourseStructureRoot
    {
        [JsonPropertyName("courseTitle")]
        public string CourseTitle { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("levels")]
        public List<LevelEntry> Levels { get; set; } = new List<LevelEntry>();
    }
}
