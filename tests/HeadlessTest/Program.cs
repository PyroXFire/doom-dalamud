using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using ManagedDoom;
using ManagedDoom.Audio;
using ManagedDoom.UserInput;

namespace DalamudDoom.Tests;

/// <summary>
/// Headless check of the embedded-engine path used by the Dalamud plugin:
/// engine tick -> DoomVideo byte[] frame -> save on exit. No ImGui, no game.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: HeadlessTest <iwad> [pwad]");
            return 2;
        }

        var iwad = Path.GetFullPath(args[0]);
        var persistent = args.Contains("--load");
        var saveDir = persistent
            ? Path.Combine(Path.GetTempPath(), "doom-dalamud-persist")
            : Path.Combine(Path.GetTempPath(), "doom-dalamud-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(saveDir);
        ConfigUtilities.SaveDirectory = saveDir;

        Console.WriteLine($"IWAD: {iwad}");
        Console.WriteLine($"Saves: {saveDir}");

        var cliArgs = new List<string> { "-iwad", iwad, "-skill", "3" };
        var warp = false;

        foreach (var extra in args[1..])
        {
            if (extra == "--warp")
            {
                warp = true;
                continue;
            }

            if (extra.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            cliArgs.Add("-file");
            cliArgs.Add(Path.GetFullPath(extra));
        }

        if (warp)
        {
            cliArgs.Add("-warp");
            cliArgs.Add("1");
            cliArgs.Add("1");
        }

        var cli = new CommandLineArgs(cliArgs.ToArray());

        var config = new Config
        {
            video_highresolution = false,
            video_fpsscale = 1,
            mouse_sensitivity = 8,
        };

        var content = new GameContent(cli);
        var video = new DoomVideo(config, content, () => true);

        var keys = new bool[(int)DoomKey.Count];
        var input = new PolledUserInput(
            config,
            key => keys[(int)key],
            _ => false,
            () => Vector2.Zero);

        var doom = new Doom(cli, config, content, video, NullSound.GetInstance(), NullMusic.GetInstance(), input);

        var loaded = false;
        if (persistent && File.Exists(Path.Combine(saveDir, "doomsav0.dsg")))
        {
            doom.LoadGame(0);
            loaded = true;
            Console.WriteLine("requested LoadGame(0)");
        }

        var previous = new bool[(int)DoomKey.Count];
        var frames = 0L;
        long version = -1;
        var distinctBytes = 0;

        for (var tic = 0; tic < 400; tic++)
        {
            // Walk forward for a while so we are not looking at an idle screen.
            keys[(int)DoomKey.Up] = tic > 60 && tic < 160;
            keys[(int)DoomKey.LControl] = tic % 40 == 0 && tic > 60;

            for (var i = 0; i < keys.Length; i++)
            {
                if (keys[i] != previous[i])
                {
                    doom.PostEvent(new DoomEvent(keys[i] ? EventType.KeyDown : EventType.KeyUp, (DoomKey)i));
                }
            }

            Array.Copy(keys, previous, keys.Length);

            if (doom.Update() == UpdateResult.Completed)
            {
                Console.WriteLine("engine reported completion early");
                break;
            }

            video.Render(doom, Fixed.One);
            frames++;

            if (tic == 399)
            {
                video.Consume((buffer, w, h, v) =>
                {
                    version = v;
                    var seen = new bool[256];
                    foreach (var b in buffer)
                    {
                        seen[b] = true;
                    }

                    foreach (var s in seen)
                    {
                        if (s)
                        {
                            distinctBytes++;
                        }
                    }

                    Console.WriteLine($"frame {w}x{h} version={v} bytes={buffer.Length}");
                });
            }
        }

        var state = $"{doom.State}/{doom.Game.State}";
        var saved = doom.SaveGame(0, "HEADLESS");
        for (var i = 0; i < 4; i++)
        {
            doom.Update();
            video.Render(doom, Fixed.One);
        }

        var savePath = Path.Combine(saveDir, "doomsav0.dsg");
        var saveSize = File.Exists(savePath) ? new FileInfo(savePath).Length : -1;

        Console.WriteLine($"tics={frames} lastVersion={version} distinctByteValues={distinctBytes}");
        Console.WriteLine($"state={state} saveQueued={saved} saveFileBytes={saveSize}");

        doom.Quit();
        content.Dispose();

        if (loaded)
        {
            Console.WriteLine($"loaded save -> state={doom.State}/{doom.Game.State}");
        }

        var ok = frames > 300 && version > 0 && distinctBytes > 20 && saved && saveSize > 1024;
        Console.WriteLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
        return ok ? 0 : 1;
    }
}