using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

using Dalamud.Plugin.Services;

using ManagedDoom;
using ManagedDoom.Audio;
using ManagedDoom.UserInput;

namespace DalamudDoom;

/// <summary>
/// Owns the Doom engine instance and its private simulation thread. The UI thread never
/// touches engine state directly; it only reads published frames and posts requests.
/// </summary>
public sealed class DoomHost : IDisposable
{
    private static readonly string[] PwadSeparators = { "\n", "\r", ";", "|" };

    private readonly Configuration configuration;
    private readonly InputBridge input;
    private readonly IPluginLog log;

    private Thread? thread;
    private volatile bool shutdown;
    private volatile bool saveNow;
    private volatile bool inGame;

    private bool[] previousKeys = Array.Empty<bool>();
    private bool[] currentKeys = new bool[(int)DoomKey.Count];

    public DoomHost(Configuration configuration, InputBridge input, IPluginLog log)
    {
        this.configuration = configuration;
        this.input = input;
        this.log = log;
    }

    public DoomVideo? Video { get; private set; }

    public string? LastError { get; private set; }

    public bool IsRunning => this.thread is { IsAlive: true };

    public bool InGame => this.inGame;

    public void Start()
    {
        if (this.IsRunning)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(this.configuration.IwadPath) || !File.Exists(this.configuration.IwadPath))
        {
            this.LastError = "No IWAD selected. Point the plugin at your DOOM.WAD or DOOM2.WAD first.";
            return;
        }

        this.LastError = null;
        this.shutdown = false;
        this.saveNow = false;
        this.previousKeys = Array.Empty<bool>();
        this.currentKeys = new bool[(int)DoomKey.Count];

        this.thread = new Thread(this.Run)
        {
            IsBackground = true,
            Name = "DalamudDoom.Engine",
            Priority = ThreadPriority.AboveNormal,
        };
        this.thread.Start();
    }

    public void RequestSave() => this.saveNow = true;

    public void Stop() => this.shutdown = true;

    private void Run()
    {
        Doom? doom = null;
        GameContent? content = null;
        DoomVideo? video = null;

        try
        {
            Directory.CreateDirectory(this.configuration.SaveDirectory);
            ConfigUtilities.SaveDirectory = this.configuration.SaveDirectory;

            var args = new CommandLineArgs(this.BuildArgs());
            var config = new Config
            {
                mouse_sensitivity = Math.Clamp(this.configuration.MouseSensitivity, 1, 9),
                video_highresolution = this.configuration.HighResolution,
                video_fpsscale = Math.Clamp(this.configuration.FpsScale, 1, 2),
                video_gammacorrection = this.configuration.GammaCorrection,
            };

            content = new GameContent(args);
            video = new DoomVideo(config, content, () => !this.shutdown)
            {
                GammaCorrectionLevel = Math.Clamp(this.configuration.GammaCorrection, 0, 10),
            };
            this.Video = video;

            var userInput = new PolledUserInput(
                config,
                key => this.currentKeys[(int)key],
                button => this.input.IsMouseButtonPressed(button),
                () => this.input.TakeMouseDelta(),
                () => this.input.MoveAxes(),
                () => this.input.TurnAxis());

            doom = new Doom(args, config, content, video, NullSound.GetInstance(), NullMusic.GetInstance(), userInput);

            if (this.configuration.AutoLoadAutosave &&
                File.Exists(Path.Combine(ConfigUtilities.GetSaveDirectory(), "doomsav0.dsg")))
            {
                try
                {
                    doom.LoadGame(0);
                }
                catch (Exception ex)
                {
                    this.log.Warning(ex, "Could not load the Doom autosave; starting a new game.");
                }
            }

            var step = TimeSpan.FromSeconds(1.0 / (35.0 * config.video_fpsscale));
            var stopwatch = Stopwatch.StartNew();
            var nextFrame = step;
            var lastAutoSave = stopwatch.Elapsed;
            var autoSaveInterval = this.configuration.AutoSaveMinutes > 0
                ? TimeSpan.FromMinutes(this.configuration.AutoSaveMinutes)
                : Timeout.InfiniteTimeSpan;

            while (!this.shutdown)
            {
                var keys = this.input.SnapshotKeys();
                for (var i = 0; i < keys.Length; i++)
                {
                    if (i >= this.previousKeys.Length || keys[i] != this.previousKeys[i])
                    {
                        doom.PostEvent(new DoomEvent(keys[i] ? EventType.KeyDown : EventType.KeyUp, (DoomKey)i));
                    }
                }

                this.previousKeys = keys;
                this.currentKeys = keys;

                if (doom.Update() == UpdateResult.Completed)
                {
                    break;
                }

                this.inGame = doom.State == DoomState.Game && doom.Game.State == GameState.Level;
                video.Render(doom, Fixed.One);

                if (this.saveNow)
                {
                    this.saveNow = false;
                    SaveNow(doom, "QUICKSAVE");
                }

                if (autoSaveInterval != Timeout.InfiniteTimeSpan &&
                    stopwatch.Elapsed - lastAutoSave >= autoSaveInterval)
                {
                    lastAutoSave = stopwatch.Elapsed;
                    SaveNow(doom, "AUTOSAVE");
                }

                var remaining = nextFrame - stopwatch.Elapsed;
                if (remaining > TimeSpan.FromMilliseconds(1))
                {
                    Thread.Sleep(remaining);
                }
                else if (remaining < TimeSpan.Zero)
                {
                    nextFrame = stopwatch.Elapsed;
                }

                nextFrame += step;
            }

            if (this.configuration.SaveOnExit && doom != null)
            {
                SaveNow(doom, "EXITSAVE");
            }

            doom?.Quit();
        }
        catch (Exception ex)
        {
            this.LastError = ex.ToString();
            this.log.Error(ex, "Doom engine thread crashed.");
        }
        finally
        {
            this.inGame = false;
            this.Video = null;
            video = null;
            content?.Dispose();
            this.input.ClearTransient();
        }
    }

    private static void SaveNow(Doom doom, string description)
    {
        // Save is queued as a game action and only hits disk on the next tic, so pump a few.
        if (!doom.SaveGame(0, description))
        {
            return;
        }

        for (var i = 0; i < 4; i++)
        {
            if (doom.Update() == UpdateResult.Completed)
            {
                break;
            }
        }
    }

    private string[] BuildArgs()
    {
        var list = new List<string> { "-iwad", this.configuration.IwadPath };

        var pwads = this.configuration.PwadPaths.Split(
            PwadSeparators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (pwads.Length > 0)
        {
            list.Add("-file");
            list.AddRange(pwads);
        }

        return list.ToArray();
    }

    public void Dispose()
    {
        this.shutdown = true;
        this.thread?.Join(TimeSpan.FromSeconds(5));
        this.thread = null;
    }
}