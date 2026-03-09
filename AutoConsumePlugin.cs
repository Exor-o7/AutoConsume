using Eco.Core.Plugins;
using Eco.Core.Plugins.Interfaces;
using Eco.Core.Utils;
using Eco.Gameplay.Aliases;
using Eco.Gameplay.GameActions;
using Eco.Gameplay.Items;
using Eco.Gameplay.Players;
using Eco.Gameplay.Property;
using Eco.Shared.Localization;
using Eco.Shared.Logging;
using Eco.Shared.Utils;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace AutoConsume
{
    /// <summary>
    /// AutoConsume — automatically eats food from a player's toolbar (the bottom
    /// row visible in the backpack UI) whenever they contribute labor and their
    /// calories fall below a configurable threshold.
    ///
    /// Drop the compiled AutoConsume.dll into:
    ///   [EcoServer]\Eco_Data\Server\Mods\AutoConsume\AutoConsume.dll
    ///
    /// Configuration is available in the ECO admin panel under Configs → AutoConsume
    /// or by editing Configs/AutoConsume.eco on the server.
    /// </summary>
    public class AutoConsumePlugin
        : IModKitPlugin,
          IInitializablePlugin,
          IShutdownablePlugin,
          IGameActionAware
    {
        // ── Singleton ──────────────────────────────────────────────────────────────
        public static AutoConsumePlugin Obj { get; private set; } = null!;

        // ── Config ─────────────────────────────────────────────────────────────────
        // Loaded from Mods/AutoConsume/AutoConsume.json on startup.
        // If the file doesn't exist it is created with default values.
        public AutoConsumeConfig Config { get; private set; } = new();
        private string configPath = string.Empty;

        // Persisted player preferences file: Mods/AutoConsume/AutoConsumePlayerPrefs.json
        // Stores the set of players who have disabled the mod for themselves.
        private string playerPrefsPath = string.Empty;

        // ── IServerPlugin ──────────────────────────────────────────────────────────
        public string GetCategory() => "Mods";

        // ── Per-player toolbar rotation index ─────────────────────────────────────
        // Tracks which food slot to eat from next so foods alternate evenly.
        private readonly ConcurrentDictionary<string, int> nextSlotIndex = new();

        // ── Per-player opt-out ────────────────────────────────────────────────────
        // Players toggle this via /autoconsume. True = enabled (default).
        private readonly ConcurrentDictionary<string, bool> playerEnabled = new();

        public bool IsPlayerEnabled(string userName) => playerEnabled.GetOrAdd(userName, true);
        public void SetPlayerEnabled(string userName, bool enabled)
        {
            playerEnabled[userName] = enabled;
            SavePlayerPrefs();
        }

        // ── IModKitPlugin ──────────────────────────────────────────────────────────
        public string GetStatus() =>
            $"Running | threshold={Config.CalorieThresholdPercent}% | toolbarOnly={Config.ToolbarOnly}";

        public override string ToString() => "AutoConsume";

        // ── Lifecycle ──────────────────────────────────────────────────────────────
        public void Initialize(TimedTask timer)
        {
            Obj = this;

            // Load config from a plain JSON file — no ECO storage system involved.
            // File lives at:  Mods/AutoConsume/AutoConsume.json
            configPath     = Path.Combine("Mods", "AutoConsume", "AutoConsume.json");
            playerPrefsPath = Path.Combine("Mods", "AutoConsume", "AutoConsumePlayerPrefs.json");
            LoadConfig();
            LoadPlayerPrefs();

            // Register as a global game action listener.
            ActionUtil.AddListener(this);
            Log.WriteLineLoc($"[AutoConsume] Initialized — auto-eating below {Config.CalorieThresholdPercent}% calories.");
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    Config = JsonSerializer.Deserialize<AutoConsumeConfig>(json) ?? new AutoConsumeConfig();
                    // Re-write the file so any newly added fields appear with their defaults.
                    File.WriteAllText(configPath,
                        JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    // Write defaults so the user can edit them.
                    Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
                    File.WriteAllText(configPath,
                        JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch (Exception ex)
            {
                Log.WriteWarningLineLoc($"[AutoConsume] Failed to load config, using defaults: {ex.Message}", 1);
            }
        }

        private void LoadPlayerPrefs()
        {
            try
            {
                if (!File.Exists(playerPrefsPath)) return;
                var disabledNames = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(playerPrefsPath));
                if (disabledNames == null) return;
                foreach (var name in disabledNames)
                    playerEnabled[name] = false;
            }
            catch (Exception ex)
            {
                Log.WriteWarningLineLoc($"[AutoConsume] Failed to load player prefs: {ex.Message}", 1);
            }
        }

        public void SavePlayerPrefs()
        {
            try
            {
                // Only persist players who have explicitly disabled the mod.
                var disabledNames = playerEnabled
                    .Where(kv => !kv.Value)
                    .Select(kv => kv.Key)
                    .ToList();
                Directory.CreateDirectory(Path.GetDirectoryName(playerPrefsPath)!);
                File.WriteAllText(playerPrefsPath,
                    JsonSerializer.Serialize(disabledNames, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Log.WriteWarningLineLoc($"[AutoConsume] Failed to save player prefs: {ex.Message}", 1);
            }
        }

        public async Task ShutdownAsync()
        {
            ActionUtil.RemoveListener(this);
            await Task.CompletedTask;
        }

        // ── IGameActionAware ───────────────────────────────────────────────────────

        /// <summary>Called by the game engine for every game action on the server.</summary>
        public void ActionPerformed(GameAction action)
        {
            // LaborWorkOrderAction fires whenever a player contributes labor to a
            // work order. It implements IUserGameAction so Citizen is always set.
            // NOTE: it does NOT implement ICalorieConsumingAction in ECO 0.12.x.
            if (action is not LaborWorkOrderAction laborAction) return;

            var citizen = laborAction.Citizen;
            if (citizen == null) return;

            TryAutoConsumeFood(citizen);
        }

        /// <summary>
        /// Return Succeeded so this listener never blocks or overrides auth on
        /// any action.  We are purely observing, not gating.
        /// </summary>
        public LazyResult ShouldOverrideAuth(IAlias? alias, IOwned? owned, GameAction? action)
            => LazyResult.Succeeded;

        // ── Core logic ─────────────────────────────────────────────────────────────

        private void TryAutoConsumeFood(User user)
        {
            if (Config.CalorieThresholdPercent <= 0f) return; // feature disabled
            if (!IsPlayerEnabled(user.Name))              return; // player opted out

            var stomach = user.Stomach;
            if (stomach == null) return;

            float thresholdCal = stomach.MaxCalories * (Config.CalorieThresholdPercent / 100f);

            // Keep eating until calories are back above the threshold or no food remains.
            while (stomach.Calories <= thresholdCal)
            {
                bool ate = TryEatFromToolbar(user);

                if (!ate && !Config.ToolbarOnly)
                    ate = TryEatFromBackpack(user);

                if (!ate) break; // no food left
            }
        }

        /// <summary>
        /// Scans the player's toolbar (the bottom row shown in the backpack UI)
        /// left-to-right and eats the first food item found.
        /// </summary>
        private bool TryEatFromToolbar(User user)
        {
            var toolbar = user.Inventory?.Toolbar;
            if (toolbar == null) return false;

            // Build a list of only the slots that currently contain eligible food.
            // Rotation is over this list, so 2 foods = alternates between 2,
            // 3 foods = cycles all 3, etc.
            var foodStacks = System.Linq.Enumerable.ToList(
                System.Linq.Enumerable.Where(toolbar.Stacks,
                    s => s != null && s.Quantity > 0 && s.Item is FoodItem f && IsEligibleFood(f)));

            if (foodStacks.Count == 0) return false;

            int startIdx = nextSlotIndex.GetOrAdd(user.Name, 0) % foodStacks.Count;
            var stack    = foodStacks[startIdx];
            var food     = (FoodItem)stack.Item!;

            var result = toolbar.TryRemoveItems(food.GetType(), 1, user);
            if (!result.Success) return false;

            // Advance to the next food slot (wraps back to 0 after the last one).
            nextSlotIndex[user.Name] = (startIdx + 1) % foodStacks.Count;

            EatFood(user, food);
            return true;
        }

        private bool TryEatFromBackpack(User user)
        {
            var backpack = user.Inventory?.Backpack;
            if (backpack == null) return false;

            var foodStacks = System.Linq.Enumerable.ToList(
                System.Linq.Enumerable.Where(backpack.Stacks,
                    s => s != null && s.Quantity > 0 && s.Item is FoodItem f && IsEligibleFood(f)));

            if (foodStacks.Count == 0) return false;

            // Use a separate rotation key for backpack so it alternates independently.
            string key = user.Name + "_backpack";
            int startIdx = nextSlotIndex.GetOrAdd(key, 0) % foodStacks.Count;
            var stack    = foodStacks[startIdx];
            var food     = (FoodItem)stack.Item!;

            var result = backpack.TryRemoveItems(food.GetType(), 1, user);
            if (!result.Success) return false;

            nextSlotIndex[key] = (startIdx + 1) % foodStacks.Count;

            EatFood(user, food);
            return true;
        }

        /// <summary>
        /// Returns true if <paramref name="food"/> should be considered for auto-consumption.
        /// Excluded: seeds, raw tagged food (crops/vegetables/fruits), raw meat/fish items,
        /// and unprocessed meat ingredients that have no tags but shouldn't be eaten directly.
        /// </summary>
        private static readonly HashSet<string> ExcludedTypeNames = new()
        {
            "PreparedMeatItem",  // raw meat ingredient, not a finished dish
            "ScrapMeatItem",     // low-quality raw meat byproduct
            "PrimeCutItem",      // uncooked premium meat cut
        };

        private static bool IsEligibleFood(FoodItem food)
        {
            // Seeds (SeedItem subclass) should never be auto-eaten.
            if (food is SeedItem) return false;

            var type = food.GetType();

            // Explicitly excluded untagged raw ingredients.
            if (ExcludedTypeNames.Contains(type.Name)) return false;

            // Items tagged "Raw Food" cover raw crops, vegetables, fruits, grains, etc.
            if (TagUtils.HasTag(type, "Raw Food")) return false;

            // Raw meat/fish items (RawMeatItem, RawFishItem, RawBaconItem, etc.) carry no
            // tags, so catch them by name prefix.
            if (type.Name.StartsWith("Raw", StringComparison.Ordinal)) return false;

            return true;
        }

        /// <summary>
        /// Tells the player's stomach to digest <paramref name="food"/>.
        /// ECO 0.12.x exposes <c>Stomach.EatFood(FoodItem)</c>; if the build
        /// fails here, check the exact method signature in the reference assemblies.
        /// </summary>
        private void EatFood(User user, FoodItem food)
        {
            // Stomach.Eat(food, out message, force, table) — 0.12.x signature.
            // force=true bypasses stomach-full checks so labor never gets stuck.
            // table=null means no crafting table bonus applies.
            user.Stomach.Eat(food, out string eatMsg, true, null);

            if (Config.NotifyPlayer)
            {
                user.MsgLocStr(Localizer.DoStr(
                    $"[AutoConsume] Ate 1x {food.DisplayName} " +
                    $"(+{food.Calories:F0} cal). " +
                    $"Calories: {user.Stomach.Calories:F0} / {user.Stomach.MaxCalories:F0}"));
            }
        }
    }
}
