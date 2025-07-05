namespace Omnieye.Bot.CoreModels
{
    public class DifficultyLevel
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
    }
}
