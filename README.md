# CS2-Simple-Nightvision

## How to use

1. Build the plugin and place `CS2SimpleNightvision.dll` in your CounterStrikeSharp plugins directory.
2. CounterStrikeSharp: https://github.com/roflmuffin/CounterStrikeSharp
3. Use `!nvs` or `!nvg` to toggle nightvision.
4. Use `!nvi <value>` to change intensity while nightvision is enabled.
5. No special startup order is required; the plugin initializes player state lazily after the server is ready.

Config values:

- `Chat Prefix`
- `Default Intensity`
- `Minimum Intensity`
- `Maximum Intensity`

## Build

Open `CS2-Simple-Nightvision.sln` in Visual Studio or Rider, or build from the terminal:

```powershell
dotnet restore
dotnet build -c Release
```
