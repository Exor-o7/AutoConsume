# AutoConsume (ECO Mod)

AutoConsume automatically eats food for a player when they perform labor and their calories drop below a configured threshold.

The mod listens for labor work-order actions, checks the player’s calories, and consumes eligible food until calories are back above the threshold.

## What it does

- Triggers when a player contributes labor (`LaborWorkOrderAction`).
- Checks current calories against `calorieThresholdPercent`.
- Eats food from the toolbar first.
- Optionally falls back to backpack food when `toolbarOnly` is `false`.
- Rotates between available food stacks so consumption is spread across items.
- Supports per-player toggle with `/autoconsume` (alias: `/ac`).

## Food selection rules

AutoConsume **does not** consume:

- Seeds (`SeedItem`)
- Items tagged as `Raw Food`
- Item types starting with `Raw` (raw meat/fish variants)
- Explicit exclusions: `PreparedMeatItem`, `ScrapMeatItem`, `PrimeCutItem`

Everything else that is a valid `FoodItem` can be auto-consumed.

## Install

1. Build the project in `Release`.
2. Copy `AutoConsume.dll` to:
   - `Eco_Data/Server/Mods/AutoConsume/AutoConsume.dll`
3. Restart the server.

## Configuration

Config file is created automatically at first startup:

- `Mods/AutoConsume/AutoConsume.json`

Available settings:

- `calorieThresholdPercent` (float, default `75`): eat when calories are at or below this % of max. Set `0` to disable.
- `toolbarOnly` (bool, default `true`): only use toolbar food; set `false` to also search backpack.
- `notifyPlayer` (bool, default `true`): send a chat message each time food is auto-consumed.

Player toggle preferences are saved in:

- `Mods/AutoConsume/AutoConsumePlayerPrefs.json`

## Chat command

- `/autoconsume` or `/ac`
  - Toggles AutoConsume on/off for that player.

## Build & release

This repo includes a GitHub Actions workflow:

- `.github/workflows/release.yml`

When you push a tag like `v1.0.0`, it builds Release output and uploads a ZIP asset to a GitHub Release.
