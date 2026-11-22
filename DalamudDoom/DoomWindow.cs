using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace DalamudDoom;

public sealed class DoomWindow
{
    private const int DxgiR8G8B8A8Unorm = 28;

    private readonly Configuration configuration;
    private readonly DoomHost host;
    private readonly InputBridge input;
    private readonly IKeyState keyState;
    private readonly ITextureProvider textures;
    private readonly Action saveConfiguration;
    private readonly Action startEngine;

    private bool open;
    private IDalamudTextureWrap? texture;
    private long shownVersion = -1;
    private string wadCandidates = string.Empty;
    private List<string>? candidates;
    private bool showSettings;
    private bool autoDetected;

    public DoomWindow(
        Configuration configuration,
        DoomHost host,
        InputBridge input,
        IKeyState keyState,
        ITextureProvider textures,
        Action saveConfiguration,
        Action startEngine)
    {
        this.configuration = configuration;
        this.host = host;
        this.input = input;
        this.keyState = keyState;
        this.textures = textures;
        this.saveConfiguration = saveConfiguration;
        this.startEngine = startEngine;
    }

    public void Show()
    {
        this.open = true;
        this.startEngine();
    }

    public void Hide()
    {
        this.open = false;
        this.OnClosed();
    }

    public void Draw()
    {
        if (!this.open)
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(1024f, 800f), ImGuiCond.FirstUseEver);
        const ImGuiWindowFlags Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        if (ImGui.Begin("Doom###DalamudDoomWindow", ref this.open, Flags))
        {
            if (this.host.IsRunning)
            {
                this.DrawGame();
            }
            else
            {
                this.DrawSetup();
            }
        }

        ImGui.End();

        if (!this.open)
        {
            this.OnClosed();
        }
    }

    private void OnClosed()
    {
        this.DropTexture();
        this.host.RequestSave();
        this.host.Stop();
    }

    private void DropTexture()
    {
        this.texture?.Dispose();
        this.texture = null;
        this.shownVersion = -1;
    }

    private void DrawGame()
    {
        if (this.host.LastError != null)
        {
            ImGui.TextUnformatted("The Doom engine stopped:");
            ImGui.TextUnformatted(this.host.LastError);
            if (ImGui.Button("Reset engine"))
            {
                this.host.Stop();
                this.DropTexture();
            }

            return;
        }

        // Input first: the engine polls the last snapshot on its next tic.
        var hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.None);
        var focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.None);
        var mouseLook = hovered && this.configuration.MouseLook;
        var io = ImGui.GetIO();
        this.input.Capture(this.keyState, mouseLook ? io.MouseDelta : Vector2.Zero, mouseLook);

        if (hovered || focused)
        {
            // Stop FFXIV from eating the keys we want for Doom.
            ImGui.SetNextFrameWantCaptureKeyboard(true);
        }

        var available = ImGui.GetContentRegionAvail();
        var height = available.Y - 40f;
        var width = height / 0.75f;
        if (width > available.X)
        {
            width = available.X;
            height = width * 0.75f;
        }

        this.UploadFrame();

        if (this.texture != null)
        {
            ImGui.Image(this.texture.Handle, new Vector2(width, height));
        }
        else
        {
            ImGui.TextUnformatted("Waiting for the first frame...");
        }

        ImGui.Separator();
        if (ImGui.Button("Save now"))
        {
            this.host.RequestSave();
        }

        ImGui.SameLine();
        if (ImGui.Button("Close window"))
        {
            this.open = false;
        }

        ImGui.SameLine();
        ImGui.TextUnformatted(this.host.InGame ? "in game" : "menus / intermission");

        ImGui.SameLine();
        if (ImGui.Button("Settings"))
        {
            this.showSettings = !this.showSettings;
        }

        if (this.showSettings)
        {
            this.DrawSettings();
        }
    }

    private void UploadFrame()
    {
        var video = this.host.Video;
        if (video == null)
        {
            return;
        }

        video.Consume((buffer, width, height, version) =>
        {
            if (version == this.shownVersion)
            {
                return;
            }

            this.shownVersion = version;
            this.texture?.Dispose();
            this.texture = this.textures.CreateFromRaw(
                new RawImageSpecification(width, height, DxgiR8G8B8A8Unorm),
                buffer,
                "DalamudDoom frame");
        });
    }

    private void DrawSetup()
    {
        ImGui.TextUnformatted("Doom needs an IWAD. Point me at DOOM.WAD, DOOM2.WAD or FREEDOOM2.WAD.");
        ImGui.TextUnformatted("Vanilla-format WADs only - no Boom/MBF/ZDoom maps.");

        if (this.host.LastError != null)
        {
            ImGui.Separator();
            ImGui.TextUnformatted(this.host.LastError);
        }

        ImGui.Separator();
        var iwad = this.configuration.IwadPath;
        if (ImGui.InputText("IWAD path", ref iwad, 512))
        {
            this.configuration.IwadPath = iwad;
            this.saveConfiguration();
        }

        if (!this.autoDetected)
        {
            // First open: look for Steam copies and pick a sensible default so the plugin is
            // usable without hunting for the WAD path by hand.
            this.autoDetected = true;
            this.candidates = WadFinder.Find();
            if (string.IsNullOrWhiteSpace(this.configuration.IwadPath) && this.candidates.Count > 0)
            {
                this.configuration.IwadPath = this.candidates[0];
                this.saveConfiguration();
            }
        }

        if (ImGui.Button("Search this machine for WADs"))
        {
            this.candidates = WadFinder.Find();
        }

        if (this.candidates != null)
        {
            if (this.candidates.Count == 0)
            {
                ImGui.TextUnformatted("Nothing found - type the path manually.");
            }

            foreach (var candidate in this.candidates)
            {
                var label = (WadFinder.IsIwad(candidate) ? "[IWAD] " : "[PWAD] ") + candidate;
                if (ImGui.Selectable(label))
                {
                    this.configuration.IwadPath = candidate;
                    this.saveConfiguration();
                }
            }
        }

        var pwads = this.configuration.PwadPaths;
        if (ImGui.InputText("Extra PWADs (one per line)", ref pwads, 2048))
        {
            this.configuration.PwadPaths = pwads;
            this.saveConfiguration();
        }

        this.DrawSettings();

        ImGui.Separator();
        if (ImGui.Button("Start Doom"))
        {
            this.startEngine();
        }
    }

    private void DrawSettings()
    {
        var changed = false;

        var highRes = this.configuration.HighResolution;
        if (ImGui.Checkbox("640x400 (otherwise 320x200)", ref highRes))
        {
            this.configuration.HighResolution = highRes;
            changed = true;
        }

        var mouseLook = this.configuration.MouseLook;
        if (ImGui.Checkbox("Mouse look", ref mouseLook))
        {
            this.configuration.MouseLook = mouseLook;
            changed = true;
        }

        var autoLoad = this.configuration.AutoLoadAutosave;
        if (ImGui.Checkbox("Continue from autosave on start", ref autoLoad))
        {
            this.configuration.AutoLoadAutosave = autoLoad;
            changed = true;
        }

        var saveOnExit = this.configuration.SaveOnExit;
        if (ImGui.Checkbox("Autosave when the window closes", ref saveOnExit))
        {
            this.configuration.SaveOnExit = saveOnExit;
            changed = true;
        }

        var minutes = this.configuration.AutoSaveMinutes;
        if (ImGui.SliderInt("Minutes between autosaves (0 = off)", ref minutes, 0, 30))
        {
            this.configuration.AutoSaveMinutes = minutes;
            changed = true;
        }

        var sensitivity = this.configuration.MouseSensitivity;
        if (ImGui.SliderInt("Mouse sensitivity", ref sensitivity, 1, 9))
        {
            this.configuration.MouseSensitivity = sensitivity;
            changed = true;
        }

        if (changed)
        {
            this.saveConfiguration();
        }
    }
}
