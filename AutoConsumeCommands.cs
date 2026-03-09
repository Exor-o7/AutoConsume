using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.Messaging.Chat.Commands;
using Eco.Shared.Localization;

namespace AutoConsume
{
    /// <summary>
    /// Registers the /autoconsume chat command so players can toggle
    /// auto-consumption on or off for themselves at any time.
    /// </summary>
    [ChatCommandHandler]
    public class AutoConsumeCommands
    {
        /// <summary>
        /// Toggle AutoConsume on or off for yourself.
        /// Usage: /autoconsume
        /// </summary>
        [ChatCommand("autoconsume", "ac", ChatAuthorizationLevel.User)]
        public static void ToggleAutoConsume(User user)
        {
            var plugin = AutoConsumePlugin.Obj;
            if (plugin == null)
            {
                user.MsgLocStr(Localizer.DoStr("[AutoConsume] Mod is not loaded."));
                return;
            }

            bool nowEnabled = !plugin.IsPlayerEnabled(user.Name);
            plugin.SetPlayerEnabled(user.Name, nowEnabled);

            string state = nowEnabled ? "enabled" : "disabled";
            user.MsgLocStr(Localizer.DoStr($"AutoConsume {state}."));
        }
    }
}
