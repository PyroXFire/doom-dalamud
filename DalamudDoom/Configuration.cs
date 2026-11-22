using System;

using Dalamud.Configuration;

namespace DalamudDoom;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Absolute path to DOOM.WAD / DOOM2.WAD / FREEDOOM2.WAD.</summary>
    public string IwadPath { get; set; } = string.Empty;

    /// <summary>Additional PWAD/PK3 paths, one per line. Vanilla-format only.</summary>
    public string PwadPaths { get; set; } = string.Empty;

    /// <summary>Where doomsav0.dsg lives. Defaults to the plugin config directory.</summary>
    public string SaveDirectory { get; set; } = string.Empty;

    /// <summary>Render at 640x400 instead of 320x200. Applies on next engine start.</summary>
    public bool HighResolution { get; set; } = true;

    /// <summary>Read the game's own gamepad state and map it onto Doom controls.</summary>
    public bool ControllerEnabled { get; set; } = true;

    /// <summary>Stick deadzone, 0..0.5.</summary>
    public float ControllerDeadzone { get; set; } = 0.15f;

    /// <summary>Write an autosave when the window is closed or the plugin unloads.</summary>
    public bool SaveOnExit { get; set; } = true;

    /// <summary>Minutes between periodic autosaves while playing. 0 disables.</summary>
    public int AutoSaveMinutes { get; set; } = 5;

    /// <summary>Load doomsav0.dsg when the engine starts.</summary>
    public bool AutoLoadAutosave { get; set; } = true;

    public bool MouseLook { get; set; } = true;

    public int MouseSensitivity { get; set; } = 8;

    public int GammaCorrection { get; set; } = 2;

    public bool OpenWindowOnStart { get; set; }

    public int FpsScale { get; set; } = 1;
}