using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace CS2SimpleNightvision;

public class CS2SimpleNightvision : BasePlugin, IPluginConfig<CS2SimpleNightvisionConfig>
{
    private const float NormalExposure = 1.0f;

    public override string ModuleName => "CS2SimpleNightvision";
    public override string ModuleAuthor => "kaish";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleDescription => "Standalone temporary nightvision plugin.";

    private readonly Dictionary<int, PlayerNightvisionState> _playerStates = [];
    private readonly Dictionary<int, CPostProcessingVolume> _postProcessVolumes = [];

    public CS2SimpleNightvisionConfig Config { get; set; } = new();

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.CheckTransmit>(OnCheckTransmit);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
    }

    public override void Unload(bool hotReload)
    {
        RemoveListener<Listeners.CheckTransmit>(OnCheckTransmit);
        RemoveListener<Listeners.OnMapStart>(OnMapStart);
        CleanupAllPostProcessing();
        _playerStates.Clear();
    }

    public void OnConfigParsed(CS2SimpleNightvisionConfig config)
    {
        config.MinimumIntensity = Math.Max(0.0f, config.MinimumIntensity);

        if (config.MaximumIntensity < config.MinimumIntensity)
            config.MaximumIntensity = config.MinimumIntensity;

        config.DefaultIntensity = Math.Clamp(config.DefaultIntensity, config.MinimumIntensity, config.MaximumIntensity);

        if (string.IsNullOrWhiteSpace(config.ChatPrefix))
            config.ChatPrefix = "[Nightvision]";

        Config = config;

        foreach (var state in _playerStates.Values)
            state.Intensity = ClampIntensity(state.Intensity);
    }

    [ConsoleCommand("css_nvs")]
    [ConsoleCommand("css_nvg")]
    public void OnToggleNightvision(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (!IsAliveHumanPlayer(player))
        {
            Reply(player, commandInfo, $"{Config.ChatPrefix} You must be alive to toggle nightvision.");
            return;
        }

        var commandPlayer = player;
        var state = GetOrCreatePlayerState(player);
        state.Enabled = !state.Enabled;

        if (state.Enabled)
        {
            if (!EnablePlayerPostProcessing(commandPlayer, state))
            {
                state.Enabled = false;
                Reply(player, commandInfo, $"{Config.ChatPrefix} Failed to create nightvision effect.");
                return;
            }
        }
        else
        {
            DisablePlayerPostProcessing(player.Slot);
        }

        var status = state.Enabled ? "enabled" : "disabled";
        Reply(player, commandInfo, $"{Config.ChatPrefix} Nightvision {status}.");
    }

    [ConsoleCommand("css_nvi")]
    public void OnSetNightvisionIntensity(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (!IsAliveHumanPlayer(player))
        {
            Reply(player, commandInfo, $"{Config.ChatPrefix} You must be alive to change nightvision intensity.");
            return;
        }

        var commandPlayer = player;
        var state = GetOrCreatePlayerState(player);
        if (!state.Enabled)
        {
            Reply(player, commandInfo, $"{Config.ChatPrefix} Turn on nightvision first.");
            return;
        }

        if (commandInfo.ArgCount < 2)
        {
            Reply(player, commandInfo, $"{Config.ChatPrefix} Usage: !nvi <intensity>");
            return;
        }

        if (!TryParseIntensity(commandInfo.GetArg(1), out var intensity))
        {
            Reply(player, commandInfo, $"{Config.ChatPrefix} Please provide a valid float value.");
            return;
        }

        state.Intensity = ClampIntensity(intensity);

        if (!EnablePlayerPostProcessing(commandPlayer, state))
        {
            state.Enabled = false;
            Reply(player, commandInfo, $"{Config.ChatPrefix} Failed to update nightvision intensity.");
            return;
        }

        var intensityText = state.Intensity.ToString("0.###", CultureInfo.InvariantCulture);
        Reply(player, commandInfo, $"{Config.ChatPrefix} Intensity set to {intensityText}.");
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult EventPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!IsHumanPlayer(player))
            return HookResult.Continue;

        GetOrCreatePlayerState(player);
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult EventPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null)
            return HookResult.Continue;

        ResetPlayerSession(player.Slot);
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult EventPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        DisableNightvision(@event.Userid);
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult EventPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        DisableNightvision(@event.Userid);
        return HookResult.Continue;
    }

    [GameEventHandler(HookMode.Post)]
    public HookResult EventRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        ResetAllNightvision();
        return HookResult.Continue;
    }

    private void OnCheckTransmit(CCheckTransmitInfoList infoList)
    {
        PruneInvalidPostProcessing();

        if (_postProcessVolumes.Count == 0)
            return;

        foreach ((CCheckTransmitInfo info, CCSPlayerController? player) in infoList)
        {
            if (!IsHumanPlayer(player))
                continue;

            var viewer = player;
            foreach (var entry in _postProcessVolumes)
            {
                if (entry.Key == viewer.Slot)
                    continue;

                info.TransmitEntities.Remove(entry.Value);
            }
        }
    }

    private void OnMapStart(string mapName)
    {
        CleanupAllPostProcessing();
        _playerStates.Clear();
    }

    private PlayerNightvisionState GetOrCreatePlayerState(CCSPlayerController player)
    {
        if (_playerStates.TryGetValue(player.Slot, out var state))
            return state;

        state = new PlayerNightvisionState
        {
            Intensity = Config.DefaultIntensity
        };

        _playerStates[player.Slot] = state;
        return state;
    }

    private bool EnablePlayerPostProcessing(CCSPlayerController player, PlayerNightvisionState state)
    {
        if (_postProcessVolumes.TryGetValue(player.Slot, out var existingPostProcessingVolume))
        {
            if (!existingPostProcessingVolume.IsValid)
            {
                _postProcessVolumes.Remove(player.Slot);
            }
            else
            {
                ApplyExposure(existingPostProcessingVolume, state.Intensity);
                return true;
            }
        }

        var postProcessingVolume = Utilities.CreateEntityByName<CPostProcessingVolume>("post_processing_volume");
        if (postProcessingVolume == null)
        {
            Console.WriteLine($"[CS2SimpleNightvision] Failed to create post_processing_volume for slot {player.Slot}");
            return false;
        }

        postProcessingVolume.Master = true;
        postProcessingVolume.FadeDuration = 0.0f;
        postProcessingVolume.ExposureControl = true;
        postProcessingVolume.MinExposure = state.Intensity;
        postProcessingVolume.MaxExposure = state.Intensity;
        postProcessingVolume.DispatchSpawn();

        _postProcessVolumes[player.Slot] = postProcessingVolume;
        return true;
    }

    private void DisablePlayerPostProcessing(int slot)
    {
        if (!_postProcessVolumes.TryGetValue(slot, out var postProcessingVolume))
            return;

        if (!postProcessingVolume.IsValid)
        {
            _postProcessVolumes.Remove(slot);
            return;
        }

        ApplyExposure(postProcessingVolume, NormalExposure);
    }

    private void RemovePlayerPostProcessing(int slot)
    {
        if (!_postProcessVolumes.TryGetValue(slot, out var postProcessingVolume))
            return;

        if (postProcessingVolume.IsValid)
        {
            postProcessingVolume.AcceptInput("Kill");
            postProcessingVolume.Remove();
        }

        _postProcessVolumes.Remove(slot);
    }

    private void DisableNightvision(CCSPlayerController? player)
    {
        if (player == null)
            return;

        if (_playerStates.TryGetValue(player.Slot, out var state))
            state.Enabled = false;

        DisablePlayerPostProcessing(player.Slot);
    }

    private void ResetAllNightvision()
    {
        foreach (var state in _playerStates.Values)
            state.Enabled = false;

        foreach (var slot in _postProcessVolumes.Keys.ToList())
            RemovePlayerPostProcessing(slot);
    }

    private void ResetPlayerSession(int slot)
    {
        RemovePlayerPostProcessing(slot);
        _playerStates.Remove(slot);
    }

    private void CleanupAllPostProcessing()
    {
        foreach (var slot in _postProcessVolumes.Keys.ToList())
            RemovePlayerPostProcessing(slot);
    }

    private void PruneInvalidPostProcessing()
    {
        foreach (var slot in _postProcessVolumes
                     .Where(entry => !entry.Value.IsValid)
                     .Select(entry => entry.Key)
                     .ToList())
        {
            _postProcessVolumes.Remove(slot);
        }
    }

    private void Reply(CCSPlayerController? player, CommandInfo commandInfo, string message)
    {
        if (player == null || !player.IsValid)
        {
            commandInfo.ReplyToCommand(message);
            return;
        }

        player.PrintToChat(message);
    }

    private float ClampIntensity(float intensity)
    {
        return Math.Clamp(intensity, Config.MinimumIntensity, Config.MaximumIntensity);
    }

    private static void ApplyExposure(CPostProcessingVolume postProcessingVolume, float exposure)
    {
        postProcessingVolume.MinExposure = exposure;
        postProcessingVolume.MaxExposure = exposure;
        Utilities.SetStateChanged(postProcessingVolume, "CPostProcessingVolume", "m_flMinExposure");
        Utilities.SetStateChanged(postProcessingVolume, "CPostProcessingVolume", "m_flMaxExposure");
    }

    private static bool TryParseIntensity(string value, out float intensity)
    {
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out intensity))
            return true;

        return float.TryParse(value, out intensity);
    }

    private static bool IsHumanPlayer([NotNullWhen(true)] CCSPlayerController? player)
    {
        return player != null &&
            player.IsValid &&
            !player.IsBot &&
            !player.IsHLTV &&
            player.Connected == PlayerConnectedState.Connected;
    }

    private static bool IsAliveHumanPlayer([NotNullWhen(true)] CCSPlayerController? player)
    {
        return IsHumanPlayer(player) &&
            player.PawnIsAlive &&
            player.TeamNum != 1;
    }

    private sealed class PlayerNightvisionState
    {
        public bool Enabled { get; set; }
        public float Intensity { get; set; }
    }
}
