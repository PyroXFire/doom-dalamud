using System;
using System.IO;

using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace DalamudDoom;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/doom";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly Configuration configuration;
    private readonly InputBridge input;
    private readonly DoomHost host;
    private readonly DoomWindow window;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ITextureProvider textures,
        IKeyState keyState,
        ICommandManager commands,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;

        this.configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        if (string.IsNullOrWhiteSpace(this.configuration.SaveDirectory))
        {
            this.configuration.SaveDirectory = Path.Combine(pluginInterface.GetPluginConfigDirectory(), "saves");
        }

        Directory.CreateDirectory(this.configuration.SaveDirectory);

        this.input = new InputBridge();
        this.host = new DoomHost(this.configuration, this.input, log);
        this.window = new DoomWindow(
            this.configuration,
            this.host,
            this.input,
            keyState,
            textures,
            this.SaveConfiguration,
            this.StartEngine);

        pluginInterface.UiBuilder.Draw += this.window.Draw;

        this.commands.AddHandler(
            CommandName,
            new CommandInfo(this.OnCommand)
            {
                HelpMessage = "Toggle the Doom window. /doom close to shut it down.",
                ShowInHelp = true,
            });

        if (this.configuration.OpenWindowOnStart)
        {
            this.window.Show();
        }
    }

    public string Name => "Doom in FFXIV";

    private void SaveConfiguration() => this.pluginInterface.SavePluginConfig(this.configuration);

    private void StartEngine()
    {
        if (!this.host.IsRunning)
        {
            this.host.Start();
        }
    }

    private void OnCommand(string command, string arguments)
    {
        if (arguments.Trim().ToLowerInvariant() is "close" or "stop")
        {
            this.window.Hide();
            return;
        }

        this.window.Show();
    }

    public void Dispose()
    {
        this.pluginInterface.UiBuilder.Draw -= this.window.Draw;
        this.commands.RemoveHandler(CommandName);
        this.window.Hide();
        this.host.Dispose();
        this.SaveConfiguration();
    }
}