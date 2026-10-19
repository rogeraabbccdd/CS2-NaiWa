using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using NaiWa.Nav;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;

namespace NaiWa;

public class NaiWaPlugin : BasePlugin, IPluginConfig<NaiWaConfig>
{
    public override string ModuleName => "NaiWa";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "Kento";
    public override string ModuleDescription => "流動奶蛙：每回合出現在地圖隨機角落的奶蛙立牌";

    private const string TargetName = "naiwa_standee";
    private const float StandeeRadius = 20;
    private const float StandeeHeight = 72;

    public NaiWaConfig Config { get; set; } = new();

    private MapNavLocator _locator = null!;
    private string _mapsDir = "";

    private string _map = "";
    private int _loadGeneration;
    private List<NavArea>? _areas;
    private SpotPicker? _picker;
    private string _navStatus = "";
    private MapSpots _spots = new();
    private readonly List<Vector3> _recent = [];
    private CHandle<CBaseModelEntity>? _naiwa;
    private readonly Dictionary<int, float> _nextUse = [];
    private readonly Dictionary<int, float> _nextHint = [];
    private bool _glow;
    private CHandle<CDynamicProp>? _glowProp;

    private IVolumeSource? _volume;

    public void OnConfigParsed(NaiWaConfig config) => Config = config;

    public override void Load(bool hotReload)
    {
        var configDir = Path.GetFullPath(Path.Combine(ModuleDirectory, "..", "..", "configs", "plugins", ModuleName));
        _mapsDir = Path.Combine(configDir, "maps");

        var gameDir = Server.GameDirectory;
        var csgoDir = Directory.Exists(Path.Combine(gameDir, "csgo", "maps")) ? Path.Combine(gameDir, "csgo") : gameDir;
        var engineDir = Path.GetDirectoryName(csgoDir)!;          // .../game
        var installDir = Path.GetDirectoryName(engineDir) ?? engineDir;
        string[] workshopRoots =
        [
            Path.Combine(installDir, "steamapps", "workshop", "content", "730"),
            Path.Combine(engineDir, "bin", "win64", "steamapps", "workshop", "content", "730"),
            Path.Combine(engineDir, "bin", "linuxsteamrt64", "steamapps", "workshop", "content", "730"),
            .. Config.ExtraWorkshopPaths,
        ];
        _locator = new MapNavLocator([Path.Combine(csgoDir, "maps")], workshopRoots,
            Path.Combine(configDir, "vpk_index.json"), msg => Logger.LogInformation("{Message}", msg));

        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnMapEnd>(() => _naiwa = null);
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            manifest.AddResource(Config.Model);
            if (!string.IsNullOrEmpty(Config.SoundEventsFile))
                manifest.AddResource(Config.SoundEventsFile);
        });
        RegisterListener<Listeners.OnPlayerButtonsChanged>(OnPlayerButtonsChanged);
        RegisterListener<Listeners.OnEntityTakeDamagePre>((entity, _) =>
            Config.Health <= 0 && IsNaiWa(entity) ? HookResult.Handled : HookResult.Continue);
        HookEntityOutput("func_breakable", "OnBreak", OnBreak);

        if (hotReload)
            OnMapStart(Server.MapName);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        try
        {
            _volume = CreateClientprefsVolume(hotReload);
        }
        catch (Exception e) when (e is FileNotFoundException or FileLoadException or TypeLoadException)
        {
            // ClientprefsApi.dll is not installed in shared/
        }
        if (_volume == null)
            Logger.LogInformation("Clientprefs not available, using DefaultVolume");
    }

    // Separate method so a missing ClientprefsApi assembly throws here, where it is caught.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private IVolumeSource? CreateClientprefsVolume(bool hotReload) => ClientprefsVolume.TryCreate(Logger, hotReload);

    public override void Unload(bool hotReload)
    {
        _volume?.Dispose();
        RemoveNaiWa();
    }

    private float GetPlayerVolume(CCSPlayerController player) => _volume?.GetVolume(player) ?? Config.DefaultVolume;

    /// <summary>Plays the laugh from the standee, once per listener at their own volume.</summary>
    private void PlayLaugh(CBaseModelEntity naiwa)
    {
        foreach (var player in Utilities.GetPlayers().Where(p => p is { IsValid: true, IsBot: false, IsHLTV: false }))
        {
            var volume = GetPlayerVolume(player);
            if (volume > 0)
                naiwa.EmitSound(Config.LaughSound, [player], volume);
        }
    }

    private void OnMapStart(string map)
    {
        _map = map;
        _areas = null;
        _picker = null;
        _naiwa = null;
        _glowProp = null;
        _recent.Clear();
        _nextUse.Clear();
        _nextHint.Clear();
        _spots = LoadSpots(map);
        LoadNav(map);
    }

    private void LoadNav(string map)
    {
        var generation = ++_loadGeneration;
        _navStatus = "loading";
        Task.Run(() =>
        {
            MapNavLocator.Result? result = null;
            try
            {
                result = _locator.Find(map);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Failed to load nav for {Map}", map);
            }

            Server.NextFrame(() =>
            {
                if (generation != _loadGeneration)
                    return;

                _areas = result?.Areas;
                _picker = null;
                _navStatus = result == null ? "no nav found, using spawn points" : $"{result.Areas.Count} areas from {result.Source}";
                Logger.LogInformation("{Map}: {Status}", map, _navStatus);

                // Spawn right away when the map is already running (hot reload, late nav load).
                if (_naiwa?.IsValid != true && SpawnPoints().Count > 0)
                    SpawnNaiWa();
            });
        });
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        Server.NextFrame(SpawnNaiWa);
        return HookResult.Continue;
    }

    private void SpawnNaiWa()
    {
        RemoveNaiWa();
        if (_spots.Disabled)
            return;

        if (!TryChoosePosition(out var pos, out var yaw))
        {
            Logger.LogWarning("{Map}: no position available for NaiWa", _map);
            return;
        }

        var entity = Utilities.CreateEntityByName<CBaseModelEntity>("func_breakable");
        if (entity == null)
            return;

        var unbreakable = Config.Health <= 0;
        var kv = new CEntityKeyValues();
        kv.SetString("targetname", TargetName);
        kv.SetString("model", Config.Model);
        kv.SetInt("health", Math.Max(0, Config.Health));
        kv.SetInt("material", Config.BreakableMaterial);
        kv.SetInt("spawnflags", unbreakable ? 1 : 0); // 1 = only break on trigger
        kv.SetVector("origin", pos.X, pos.Y, pos.Z);
        kv.SetAngle("angles", 0, yaw, 0);

        entity.DispatchSpawn(kv);
        entity.SetModel(Config.Model);
        entity.Teleport(new Vector(pos.X, pos.Y, pos.Z), new QAngle(0, yaw, 0), new Vector(0, 0, 0));
        _naiwa = new CHandle<CBaseModelEntity>(entity.EntityHandle.Raw);
        UpdateGlow();

        _recent.Add(pos);
        if (_recent.Count > Math.Max(0, Config.RecentAvoidCount))
            _recent.RemoveAt(0);

        if (Config.AnnounceSpawn)
            PrintToChatAll("naiwa.spawned");
    }

    private void RemoveNaiWa()
    {
        RemoveGlow();
        if (_naiwa?.Value is { IsValid: true } entity)
            entity.Remove();
        _naiwa = null;
    }

    /// <summary>
    /// func_breakable does not render a glow outline, so the glow goes on a see-through,
    /// non-solid prop_dynamic copy of the standee at the same spot.
    /// </summary>
    private void UpdateGlow()
    {
        RemoveGlow();
        if (!_glow || _naiwa?.Value is not { IsValid: true, AbsOrigin: not null, AbsRotation: not null } naiwa)
            return;

        var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (prop == null)
            return;

        var kv = new CEntityKeyValues();
        kv.SetString("model", Config.Model);
        kv.SetInt("solid", 0);
        kv.SetVector("origin", naiwa.AbsOrigin);
        kv.SetAngle("angles", naiwa.AbsRotation);

        prop.RenderMode = RenderMode_t.kRenderTransAlpha;
        prop.Render = Color.FromArgb(1, 255, 255, 255);
        prop.DispatchSpawn(kv);
        prop.SetModel(Config.Model);

        prop.Glow.GlowColorOverride = Color.Gold;
        prop.Glow.GlowType = 3;
        prop.Glow.GlowRange = 5000;
        prop.Glow.GlowRangeMin = 0;
        prop.Glow.GlowTeam = -1;
        Utilities.SetStateChanged(prop, "CBaseModelEntity", "m_Glow");

        _glowProp = new CHandle<CDynamicProp>(prop.EntityHandle.Raw);
    }

    private void RemoveGlow()
    {
        if (_glowProp?.Value is { IsValid: true } prop)
            prop.Remove();
        _glowProp = null;
    }

    private bool TryChoosePosition(out Vector3 position, out float yaw)
    {
        var rng = Random.Shared;
        yaw = rng.NextSingle() * 360f;

        var spots = _spots.Spots.Where(s => !_spots.Bans.Any(b => b.Covers(s.Position))).ToList();
        if (spots.Count > 0 && (rng.NextSingle() < Config.ManualSpotChance || _areas == null))
        {
            var fresh = spots.Where(s => !IsRecent(s.Position)).ToList();
            var spot = (fresh.Count > 0 ? fresh : spots)[rng.Next(fresh.Count > 0 ? fresh.Count : spots.Count)];
            position = spot.Position;
            yaw = spot.Yaw;
            return true;
        }

        var spawns = SpawnPoints();
        if (_areas != null)
        {
            _picker ??= new SpotPicker(_areas, new SpotPickerOptions
            {
                MinAreaSize = Config.MinAreaSize,
                MaxAreaHeightDelta = Config.MaxAreaHeightDelta,
                EdgeShrink = Config.EdgeShrink,
            }, spawns);

            bool Allowed(Vector3 p, bool avoidRecent) =>
                !_spots.Bans.Any(b => b.Covers(p)) &&
                spawns.All(s => Vector3.Distance(s, p) >= Config.MinDistanceFromSpawn) &&
                (!avoidRecent || !IsRecent(p));

            if (_picker.TryPick(rng, p => Allowed(p, true), 100, out position) ||
                _picker.TryPick(rng, p => Allowed(p, false), 100, out position))
                return true;
        }

        // Fallback: next to a random spawn point, offset so nobody spawns inside the standee.
        if (spawns.Count > 0)
        {
            var spawn = spawns[rng.Next(spawns.Count)];
            var angle = rng.NextSingle() * MathF.Tau;
            position = spawn + new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0) * 96;
            return true;
        }

        position = default;
        return false;
    }

    private bool IsRecent(Vector3 p) => _recent.Any(r => Vector3.Distance(r, p) < Config.RecentMinDistance);

    private static List<Vector3> SpawnPoints() =>
        Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("info_player_terrorist")
            .Concat(Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("info_player_counterterrorist"))
            .Where(e => e.AbsOrigin != null)
            .Select(e => ToVector3(e.AbsOrigin!))
            .ToList();

    private void OnPlayerButtonsChanged(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        if ((pressed & PlayerButtons.Use) == 0 || _naiwa?.Value is not { IsValid: true } naiwa || naiwa.AbsOrigin == null)
            return;
        if (!player.IsValid || player.PlayerPawn.Value is not { IsValid: true, LifeState: (byte)LifeState_t.LIFE_ALIVE } pawn)
            return;
        if (_nextUse.TryGetValue(player.Slot, out var next) && Server.CurrentTime < next)
            return;
        if (pawn.AbsOrigin == null || !IsLookingAt(pawn, ToVector3(naiwa.AbsOrigin)))
            return;

        _nextUse[player.Slot] = Server.CurrentTime + Config.UseCooldown;
        PlayLaugh(naiwa);
    }

    /// <summary>Ray from the player's eyes against a vertical cylinder around the standee.</summary>
    private bool IsLookingAt(CCSPlayerPawn pawn, Vector3 target)
    {
        var eye = ToVector3(pawn.AbsOrigin!) + new Vector3(0, 0, pawn.ViewOffset.Z);
        var pitch = pawn.EyeAngles.X * MathF.PI / 180f;
        var yaw = pawn.EyeAngles.Y * MathF.PI / 180f;
        var dir = new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch));

        float ox = eye.X - target.X, oy = eye.Y - target.Y;
        var a = dir.X * dir.X + dir.Y * dir.Y;
        var b = 2 * (ox * dir.X + oy * dir.Y);
        var c = ox * ox + oy * oy - StandeeRadius * StandeeRadius;

        float tEnter, tExit;
        if (a < 1e-6f)
        {
            if (c > 0) return false;
            tEnter = 0;
            tExit = Config.UseDistance;
        }
        else
        {
            var disc = b * b - 4 * a * c;
            if (disc < 0) return false;
            var sq = MathF.Sqrt(disc);
            tEnter = (-b - sq) / (2 * a);
            tExit = (-b + sq) / (2 * a);
        }

        tEnter = Math.Max(tEnter, 0);
        tExit = Math.Min(tExit, Config.UseDistance);
        if (tExit < tEnter) return false;

        // The ray must pass the cylinder's height range while inside its radius.
        var zEnter = eye.Z + dir.Z * tEnter - target.Z;
        var zExit = eye.Z + dir.Z * tExit - target.Z;
        return !(zEnter > StandeeHeight && zExit > StandeeHeight) && !(zEnter < 0 && zExit < 0);
    }

    private HookResult OnBreak(CEntityIOOutput output, string name, CEntityInstance activator, CEntityInstance caller, CVariant value, float delay)
    {
        if (!IsNaiWa(caller))
            return HookResult.Continue;

        _naiwa = null;
        Server.NextFrame(RemoveGlow);

        var breaker = activator is { IsValid: true, DesignerName: "player" }
            ? new CCSPlayerPawn(activator.Handle).Controller.Value?.As<CCSPlayerController>()
            : null;

        if (breaker is { IsValid: true, PawnIsAlive: true })
            Server.NextFrame(() =>
            {
                if (breaker is { IsValid: true, PawnIsAlive: true })
                    breaker.GiveNamedItem("weapon_healthshot");
            });

        if (Config.AnnounceBreak)
        {
            if (breaker is { IsValid: true })
                PrintToChatAll("naiwa.broken", breaker.PlayerName);
            else
                PrintToChatAll("naiwa.broken.unknown");
        }
        return HookResult.Continue;
    }

    private bool IsNaiWa(CEntityInstance? entity) =>
        entity is { IsValid: true } && _naiwa is { IsValid: true } && entity.Index == _naiwa.Index;

    // ---- Player commands ----

    private static readonly string[] Directions =
        ["front", "front_left", "left", "back_left", "back", "back_right", "right", "front_right"];

    [ConsoleCommand("css_naiwa", "Show which way NaiWa is")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnHintCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !Config.EnableHintCommand) return;
        if (_naiwa?.Value is not { IsValid: true, AbsOrigin: not null } naiwa)
        {
            Reply(player, command, "naiwa.admin.none");
            return;
        }
        if (player.PlayerPawn.Value is not { IsValid: true, AbsOrigin: not null } pawn) return;

        if (_nextHint.TryGetValue(player.Slot, out var next) && Server.CurrentTime < next)
        {
            Reply(player, command, "naiwa.hint.cooldown", MathF.Ceiling(next - Server.CurrentTime));
            return;
        }
        _nextHint[player.Slot] = Server.CurrentTime + Config.HintCooldown;

        var delta = ToVector3(naiwa.AbsOrigin) - ToVector3(pawn.AbsOrigin);
        // Angle relative to where the player looks; Source yaw grows counter-clockwise, so positive = left.
        var relative = MathF.Atan2(delta.Y, delta.X) * 180f / MathF.PI - pawn.EyeAngles.Y;
        relative = ((relative % 360) + 360) % 360;
        var direction = Localizer.ForPlayer(player, "naiwa.dir." + Directions[(int)MathF.Round(relative / 45f) % 8]);

        var meters = MathF.Round(new Vector2(delta.X, delta.Y).Length() * 0.0254f); // 1 unit = 1 inch
        var level = delta.Z switch
        {
            > 96 => Localizer.ForPlayer(player, "naiwa.level.above"),
            < -96 => Localizer.ForPlayer(player, "naiwa.level.below"),
            _ => "",
        };

        Reply(player, command, "naiwa.hint", direction, meters, level);
    }

    // ---- Admin commands ----

    [ConsoleCommand("css_naiwa_glow", "Toggle a see-through-walls glow on NaiWa (for testing)")]
    [RequiresPermissions("@css/generic")]
    public void OnGlowCommand(CCSPlayerController? player, CommandInfo command)
    {
        _glow = !_glow;
        UpdateGlow();
        Reply(player, command, _glow ? "naiwa.admin.glow.on" : "naiwa.admin.glow.off");
    }

    [ConsoleCommand("css_naiwa_move", "Move NaiWa to a new random position")]
    [RequiresPermissions("@css/generic")]
    public void OnMoveCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireEnabled(player, command)) return;
        SpawnNaiWa();
        Reply(player, command, _naiwa?.IsValid == true ? "naiwa.admin.moved" : "naiwa.admin.none");
    }

    [ConsoleCommand("css_naiwa_goto", "Teleport to NaiWa")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/generic")]
    public void OnGotoCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (_naiwa?.Value is not { IsValid: true, AbsOrigin: not null, AbsRotation: not null } naiwa ||
            player?.PlayerPawn.Value is not { IsValid: true } pawn)
        {
            Reply(player, command, "naiwa.admin.none");
            return;
        }

        // Stand in front of the printed side, facing it.
        var yaw = naiwa.AbsRotation.Y;
        var rad = yaw * MathF.PI / 180f;
        var pos = ToVector3(naiwa.AbsOrigin) + new Vector3(MathF.Cos(rad), MathF.Sin(rad), 0) * 80;
        pawn.Teleport(new Vector(pos.X, pos.Y, pos.Z), new QAngle(0, yaw + 180, 0), new Vector(0, 0, 0));
    }

    [ConsoleCommand("css_naiwa_add", "Add your current position as a NaiWa spot (facing where you look)")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    [RequiresPermissions("@css/generic")]
    public void OnAddCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player?.PlayerPawn.Value is not { IsValid: true, AbsOrigin: not null } pawn) return;
        var p = pawn.AbsOrigin;
        _spots.Spots.Add(new MapSpot { X = p.X, Y = p.Y, Z = p.Z, Yaw = pawn.EyeAngles.Y });
        SaveSpots();
        Reply(player, command, "naiwa.admin.added", _spots.Spots.Count);
    }

    [ConsoleCommand("css_naiwa_ban", "Ban the area around NaiWa's current position and move it")]
    [CommandHelper(usage: "[radius]")]
    [RequiresPermissions("@css/generic")]
    public void OnBanCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (_naiwa?.Value is not { IsValid: true, AbsOrigin: not null } naiwa)
        {
            Reply(player, command, "naiwa.admin.none");
            return;
        }

        var radius = float.TryParse(command.GetArg(1), out var r) && r > 0 ? r : 256f;
        var p = naiwa.AbsOrigin;
        _spots.Bans.Add(new MapBan { X = p.X, Y = p.Y, Z = p.Z, Radius = radius });
        SaveSpots();
        SpawnNaiWa();
        Reply(player, command, "naiwa.admin.banned", radius);
    }

    [ConsoleCommand("css_naiwa_toggle", "Enable or disable NaiWa on the current map")]
    [RequiresPermissions("@css/generic")]
    public void OnToggleCommand(CCSPlayerController? player, CommandInfo command)
    {
        _spots.Disabled = !_spots.Disabled;
        SaveSpots();
        if (_spots.Disabled) RemoveNaiWa(); else SpawnNaiWa();
        Reply(player, command, _spots.Disabled ? "naiwa.admin.disabled" : "naiwa.admin.enabled");
    }

    [ConsoleCommand("css_naiwa_clear", "Remove all added spots and bans on the current map")]
    [RequiresPermissions("@css/generic")]
    public void OnClearCommand(CCSPlayerController? player, CommandInfo command)
    {
        _spots.Spots.Clear();
        _spots.Bans.Clear();
        SaveSpots();
        Reply(player, command, "naiwa.admin.cleared");
    }

    [ConsoleCommand("css_naiwa_rescan", "Rebuild the workshop map index and reload the nav")]
    [RequiresPermissions("@css/generic")]
    public void OnRescanCommand(CCSPlayerController? player, CommandInfo command)
    {
        _locator.Invalidate();
        LoadNav(_map);
        Reply(player, command, "naiwa.admin.rescan");
    }

    [ConsoleCommand("css_naiwa_info", "Show NaiWa status for the current map")]
    [RequiresPermissions("@css/generic")]
    public void OnInfoCommand(CCSPlayerController? player, CommandInfo command)
    {
        var picker = _picker == null ? "-" : $"{_picker.UsableAreas}/{_picker.TotalAreas}{(_picker.ConnectivityFiltered ? "" : " (no connectivity filter)")}";
        var pos = _naiwa?.Value is { IsValid: true, AbsOrigin: not null } n ? $"{n.AbsOrigin.X:F0} {n.AbsOrigin.Y:F0} {n.AbsOrigin.Z:F0}" : "-";
        command.ReplyToCommand($"[NaiWa] map={_map} enabled={!_spots.Disabled} nav={_navStatus}");
        command.ReplyToCommand($"[NaiWa] usable areas={picker} spots={_spots.Spots.Count} bans={_spots.Bans.Count} position={pos}");
    }

    private bool RequireEnabled(CCSPlayerController? player, CommandInfo command)
    {
        if (!_spots.Disabled) return true;
        Reply(player, command, "naiwa.admin.disabled");
        return false;
    }

    // ---- Helpers ----

    private MapSpots LoadSpots(string map)
    {
        try
        {
            return MapSpots.Load(_mapsDir, map);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to read spots for {Map}", map);
            return new MapSpots();
        }
    }

    private void SaveSpots()
    {
        try
        {
            _spots.Save(_mapsDir, _map);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Failed to save spots for {Map}", _map);
        }
    }

    private void PrintToChatAll(string key, params object[] args)
    {
        foreach (var p in Utilities.GetPlayers().Where(p => p is { IsValid: true, IsBot: false }))
            p.PrintToChat(Format(p, key, args));
    }

    private void Reply(CCSPlayerController? player, CommandInfo command, string key, params object[] args)
    {
        if (player == null)
            command.ReplyToCommand("[NaiWa] " + Regex.Replace(Localizer[key, args], @"\{[a-z]+\}", ""));
        else
            player.PrintToChat(Format(player, key, args));
    }

    private string Format(CCSPlayerController player, string key, object[] args) =>
        $" {Localizer.ForPlayer(player, "naiwa.prefix")} {Localizer.ForPlayer(player, key, args)}".ReplaceColorTags();

    private static Vector3 ToVector3(Vector v) => new(v.X, v.Y, v.Z);
}
