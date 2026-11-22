using System;

using ManagedDoom;
using ManagedDoom.Video;

namespace DalamudDoom;

/// <summary>
/// IVideo implementation that renders Doom frames into a plain byte[] (RGBA, 4 bytes per pixel)
/// and publishes the newest frame to the UI thread. Double buffered so the render thread never
/// writes the buffer a consumer is reading.
/// </summary>
public sealed class DoomVideo : IVideo
{
    private readonly Renderer renderer;
    private readonly object gate = new();

    private byte[] front;
    private byte[] back;
    private long version;

    public DoomVideo(Config config, GameContent content, Func<bool> hasFocus)
    {
        this.renderer = new Renderer(config, content);
        this.front = new byte[4 * this.renderer.Width * this.renderer.Height];
        this.back = new byte[this.front.Length];
        this.FocusProvider = hasFocus;
    }

    private Func<bool> FocusProvider { get; set; }

    public int Width => this.renderer.Width;

    public int Height => this.renderer.Height;

    public void Render(Doom doom, Fixed frameFrac)
    {
        lock (this.gate)
        {
            this.renderer.Render(doom, this.back, frameFrac);

            // The engine framebuffer is column-major (index = x * height + y), the way the
            // original Doom screen buffer was laid out. Transpose it into a plain row-major
            // RGBA image so it can be uploaded straight to a texture.
            var width = this.renderer.Width;
            var height = this.renderer.Height;
            var src = this.back;
            var dst = this.front;

            for (var x = 0; x < width; x++)
            {
                var column = x * height;
                for (var y = 0; y < height; y++)
                {
                    var s = (column + y) * 4;
                    var d = (y * width + x) * 4;
                    dst[d] = src[s];
                    dst[d + 1] = src[s + 1];
                    dst[d + 2] = src[s + 2];
                    dst[d + 3] = src[s + 3];
                }
            }

            this.version++;
        }
    }

    /// <summary>Invokes <paramref name="consumer"/> with the newest frame while holding the frame lock.</summary>
    public void Consume(Action<byte[], int, int, long> consumer)
    {
        lock (this.gate)
        {
            consumer(this.front, this.renderer.Width, this.renderer.Height, this.version);
        }
    }

    public void InitializeWipe() => this.renderer.InitializeWipe();

    public bool HasFocus() => this.FocusProvider();

    public int MaxWindowSize => this.renderer.MaxWindowSize;

    public int WindowSize
    {
        get => this.renderer.WindowSize;
        set => this.renderer.WindowSize = value;
    }

    public bool DisplayMessage
    {
        get => this.renderer.DisplayMessage;
        set => this.renderer.DisplayMessage = value;
    }

    public int MaxGammaCorrectionLevel => this.renderer.MaxGammaCorrectionLevel;

    public int GammaCorrectionLevel
    {
        get => this.renderer.GammaCorrectionLevel;
        set => this.renderer.GammaCorrectionLevel = value;
    }

    public int WipeBandCount => this.renderer.WipeBandCount;

    public int WipeHeight => this.renderer.WipeHeight;
}