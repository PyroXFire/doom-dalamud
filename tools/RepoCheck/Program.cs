using System;
using System.IO;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;

internal static class Program
{
    private static int Main(string[] args)
    {
        var asm = typeof(Dalamud.Plugin.IDalamudPluginInterface).Assembly;
        var manifestType = asm.GetType("Dalamud.Plugin.Internal.Types.PluginManifest");

        Console.WriteLine($"Dalamud: {asm.GetName().Version}");
        Console.WriteLine($"PluginManifest type: {manifestType}");

        if (manifestType is null)
        {
            return 2;
        }

        foreach (var field in manifestType.GetProperties())
        {
            var req = field.GetCustomAttribute<JsonPropertyAttribute>()?.Required;
            if (req is not null && req != Required.Default)
            {
                Console.WriteLine($"  REQUIRED prop {field.Name} ({req})");
            }
        }

        foreach (var path in args)
        {
            var json = File.ReadAllText(path);
            try
            {
                var trimmed = json.TrimStart();
                var payload = trimmed.StartsWith("[")
                    ? JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JArray>(json)?.First?.ToString() ?? "null"
                    : json;

                var obj = payload == "null" ? null : JsonConvert.DeserializeObject(payload, manifestType);
                if (obj is null)
                {
                    Console.WriteLine($"{path}: deserialized to null (invalid manifest)");
                    continue;
                }

                Console.WriteLine($"{path}: OK");
                foreach (var p in new[] { "InternalName", "Name", "AssemblyVersion", "DalamudApiLevel", "DownloadLinkInstall", "ApplicableVersion" })
                {
                    var prop = manifestType.GetProperty(p, BindingFlags.Public | BindingFlags.Instance);
                    Console.WriteLine($"   {p} = {prop?.GetValue(obj)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{path}: THREW {ex.GetType().Name}: {ex.Message}");
            }
        }

        return 0;
    }
}
