using System.Text.Json.Serialization;

namespace AutoConsume
{
    /// <summary>
    /// AutoConsume configuration.
    /// Edit Mods/AutoConsume/AutoConsume.json on the server and restart to apply changes.
    /// The file is auto-created with defaults on first startup.
    /// </summary>
    public class AutoConsumeConfig
    {
        /// <summary>Eat when calories are at or below this % of max. Set to 0 to disable.</summary>
        [JsonPropertyName("calorieThresholdPercent")]
        public float CalorieThresholdPercent { get; set; } = 75f;

        /// <summary>Only search the toolbar row. If false, also searches the backpack.</summary>
        [JsonPropertyName("toolbarOnly")]
        public bool ToolbarOnly { get; set; } = true;

        /// <summary>Send a chat message to the player when food is auto-consumed.</summary>
        [JsonPropertyName("notifyPlayer")]
        public bool NotifyPlayer { get; set; } = true;
    }
}
