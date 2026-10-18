using CounterStrikeSharp.API.Core;

namespace NaiWa;

public class NaiWaConfig : BasePluginConfig
{
    /// <summary>NaiWa only appears on maps starting with one of these.</summary>
    public List<string> MapPrefixes { get; set; } = ["de_", "cs_"];

    public string Model { get; set; } = "models/naiwa/naiwa_standee.vmdl";
    public string SoundEventsFile { get; set; } = "soundevents/naiwa.vsndevts";
    public string LaughSound { get; set; } = "NaiWa.Laugh";

    /// <summary>Used when Clientprefs is missing or the player has no "Volume" cookie.</summary>
    public float DefaultVolume { get; set; } = 0.4f;

    /// <summary>0 = unbreakable.</summary>
    public int Health { get; set; } = 0;

    /// <summary>func_breakable material: 0 glass, 1 wood, 2 metal, 3 flesh, 4 cinderblock, 8 rocks, 10 none.</summary>
    public int BreakableMaterial { get; set; } = 1;

    public float UseDistance { get; set; } = 120;
    public float UseCooldown { get; set; } = 1.5f;

    /// <summary>Allow players to use !naiwa to get the direction and distance to NaiWa.</summary>
    public bool EnableHintCommand { get; set; } = true;
    public float HintCooldown { get; set; } = 10;

    public bool AnnounceSpawn { get; set; } = true;
    public bool AnnounceBreak { get; set; } = true;

    public float MinDistanceFromSpawn { get; set; } = 600;
    public int RecentAvoidCount { get; set; } = 5;
    public float RecentMinDistance { get; set; } = 800;

    /// <summary>Chance to use an admin-added spot instead of a random nav position, when the map has any.</summary>
    public float ManualSpotChance { get; set; } = 0.3f;

    public float MinAreaSize { get; set; } = 2304;
    public float MaxAreaHeightDelta { get; set; } = 12;
    public float EdgeShrink { get; set; } = 0.6f;

    /// <summary>Extra steamapps/workshop/content/730 folders to search for workshop maps.</summary>
    public List<string> ExtraWorkshopPaths { get; set; } = [];
}
