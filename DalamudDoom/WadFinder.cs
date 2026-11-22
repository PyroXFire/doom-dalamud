using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DalamudDoom;

/// <summary>
/// Finds IWADs/PWADs on the machine, Steam libraries first (the classic Doom packages keep
/// DOOM.WAD under base/ and DOOM2.WAD under base/doom2/), then common user folders.
/// </summary>
internal static class WadFinder
{
    private const int MaxResults = 60;
    private const int MaxDepth = 4;

    private static readonly string[] IwadPriority =
    {
        "doom2.wad", "doom.wad", "doom1.wad", "plutonia.wad", "tnt.wad", "freedoom2.wad", "freedoom1.wad",
    };

    public static List<string> Find()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in SteamGameDirectories())
        {
            Scan(dir, found);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var sub in new[] { "Games", "Downloads", "Documents", "Desktop", "doom", "Doom", "wads", "Wads" })
        {
            Scan(Path.Combine(home, sub), found);
        }

        return found
            .OrderBy(Score)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResults)
            .ToList();
    }

    public static bool IsIwad(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var magic = new byte[4];
            return stream.Read(magic, 0, 4) == 4 && magic[0] == (byte)'I' && magic[1] == (byte)'W';
        }
        catch
        {
            return false;
        }
    }

    private static int Score(string path)
    {
        var name = Path.GetFileName(path);
        var index = Array.FindIndex(IwadPriority, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            return index;
        }

        // The 2024 remaster keeps its own copies under rerelease/; prefer the classic files.
        var penalty = path.Contains("rerelease", StringComparison.OrdinalIgnoreCase) ? 10 : 0;

        return (IsIwad(path) ? 100 : 200) + penalty;
    }

    private static void Scan(string root, HashSet<string> found)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }

        try
        {
            Walk(new DirectoryInfo(root), 0, found);
        }
        catch
        {
            // Unreadable or offline drive - skip.
        }
    }

    private static void Walk(DirectoryInfo dir, int depth, HashSet<string> found)
    {
        if (depth > MaxDepth || found.Count >= MaxResults * 4)
        {
            return;
        }

        try
        {
            // No name pattern: on Linux (and inside Proton) "*.wad" is case-sensitive and the
            // classic Steam packages ship DOOM.WAD / DOOM2.WAD in upper case.
            foreach (var file in dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                if (found.Count >= MaxResults * 4)
                {
                    break;
                }

                if (string.Equals(file.Extension, ".wad", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(file.FullName);
                }
            }

            foreach (var sub in dir.EnumerateDirectories())
            {
                Walk(sub, depth + 1, found);
            }
        }
        catch
        {
            // Permission problems, symlink loops, disappearing drives.
        }
    }

    private static IEnumerable<string> SteamGameDirectories()
    {
        foreach (var library in SteamLibraries())
        {
            var common = Path.Combine(library, "steamapps", "common");
            if (!Directory.Exists(common))
            {
                continue;
            }

            IEnumerable<string> games;
            try
            {
                games = Directory.EnumerateDirectories(common).ToList();
            }
            catch
            {
                continue;
            }

            foreach (var game in games)
            {
                // Only dig into Doom-ish folders; a full library walk is far too slow.
                if (Path.GetFileName(game).Contains("doom", StringComparison.OrdinalIgnoreCase))
                {
                    yield return game;
                }
            }
        }
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var vdfFiles = new List<string>
        {
            Path.Combine(home, ".steam", "steam", "steamapps", "libraryfolders.vdf"),
            Path.Combine(home, ".local", "share", "Steam", "steamapps", "libraryfolders.vdf"),
            Path.Combine(home, ".steam", "debian-installation", "steamapps", "libraryfolders.vdf"),
        };

        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var vdf in vdfFiles)
        {
            if (!File.Exists(vdf))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(vdf);
            }
            catch
            {
                continue;
            }

            foreach (Match match in Regex.Matches(text, "\"([^\"]*)\""))
            {
                var path = match.Groups[1].Value;
                if (path.Length > 3 && (path.Contains('/') || path.Contains('\\')) && installed.Add(path))
                {
                    yield return path;
                }
            }
        }

        // Defaults, in case the vdf is missing or unreadable.
        foreach (var fallback in new[]
                 {
                     Path.Combine(home, ".steam", "steam"),
                     Path.Combine(home, ".local", "share", "Steam"),
                     @"C:\Program Files (x86)\Steam",
                     @"D:\SteamLibrary",
                 })
        {
            if (installed.Add(fallback))
            {
                yield return fallback;
            }
        }
    }
}