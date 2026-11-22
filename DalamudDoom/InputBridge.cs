using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin.Services;

using ManagedDoom;

namespace DalamudDoom;

/// <summary>
/// Snapshots the game's key state (plus ImGui mouse delta) into a form the
/// Doom engine thread can poll without touching game memory off-thread.
/// </summary>
public sealed class InputBridge
{
    private static readonly Dictionary<DoomKey, VirtualKey> Map = new()
    {
        { DoomKey.A, VirtualKey.A }, { DoomKey.B, VirtualKey.B }, { DoomKey.C, VirtualKey.C },
        { DoomKey.D, VirtualKey.D }, { DoomKey.E, VirtualKey.E }, { DoomKey.F, VirtualKey.F },
        { DoomKey.G, VirtualKey.G }, { DoomKey.H, VirtualKey.H }, { DoomKey.I, VirtualKey.I },
        { DoomKey.J, VirtualKey.J }, { DoomKey.K, VirtualKey.K }, { DoomKey.L, VirtualKey.L },
        { DoomKey.M, VirtualKey.M }, { DoomKey.N, VirtualKey.N }, { DoomKey.O, VirtualKey.O },
        { DoomKey.P, VirtualKey.P }, { DoomKey.Q, VirtualKey.Q }, { DoomKey.R, VirtualKey.R },
        { DoomKey.S, VirtualKey.S }, { DoomKey.T, VirtualKey.T }, { DoomKey.U, VirtualKey.U },
        { DoomKey.V, VirtualKey.V }, { DoomKey.W, VirtualKey.W }, { DoomKey.X, VirtualKey.X },
        { DoomKey.Y, VirtualKey.Y }, { DoomKey.Z, VirtualKey.Z },

        { DoomKey.Num0, VirtualKey.KEY_0 }, { DoomKey.Num1, VirtualKey.KEY_1 },
        { DoomKey.Num2, VirtualKey.KEY_2 }, { DoomKey.Num3, VirtualKey.KEY_3 },
        { DoomKey.Num4, VirtualKey.KEY_4 }, { DoomKey.Num5, VirtualKey.KEY_5 },
        { DoomKey.Num6, VirtualKey.KEY_6 }, { DoomKey.Num7, VirtualKey.KEY_7 },
        { DoomKey.Num8, VirtualKey.KEY_8 }, { DoomKey.Num9, VirtualKey.KEY_9 },

        { DoomKey.Numpad0, VirtualKey.NUMPAD0 }, { DoomKey.Numpad1, VirtualKey.NUMPAD1 },
        { DoomKey.Numpad2, VirtualKey.NUMPAD2 }, { DoomKey.Numpad3, VirtualKey.NUMPAD3 },
        { DoomKey.Numpad4, VirtualKey.NUMPAD4 }, { DoomKey.Numpad5, VirtualKey.NUMPAD5 },
        { DoomKey.Numpad6, VirtualKey.NUMPAD6 }, { DoomKey.Numpad7, VirtualKey.NUMPAD7 },
        { DoomKey.Numpad8, VirtualKey.NUMPAD8 }, { DoomKey.Numpad9, VirtualKey.NUMPAD9 },

        { DoomKey.Left, VirtualKey.LEFT }, { DoomKey.Right, VirtualKey.RIGHT },
        { DoomKey.Up, VirtualKey.UP }, { DoomKey.Down, VirtualKey.DOWN },

        { DoomKey.Escape, VirtualKey.ESCAPE }, { DoomKey.Space, VirtualKey.SPACE },
        { DoomKey.Enter, VirtualKey.RETURN }, { DoomKey.Backspace, VirtualKey.BACK },
        { DoomKey.Tab, VirtualKey.TAB }, { DoomKey.Pause, VirtualKey.PAUSE },

        { DoomKey.LShift, VirtualKey.LSHIFT }, { DoomKey.RShift, VirtualKey.RSHIFT },
        { DoomKey.LControl, VirtualKey.LCONTROL }, { DoomKey.RControl, VirtualKey.RCONTROL },
        { DoomKey.LAlt, VirtualKey.LMENU }, { DoomKey.RAlt, VirtualKey.RMENU },

        { DoomKey.PageUp, VirtualKey.PRIOR }, { DoomKey.PageDown, VirtualKey.NEXT },
        { DoomKey.Home, VirtualKey.HOME }, { DoomKey.End, VirtualKey.END },
        { DoomKey.Insert, VirtualKey.INSERT }, { DoomKey.Delete, VirtualKey.DELETE },

        { DoomKey.Hyphen, VirtualKey.OEM_MINUS }, { DoomKey.Equal, VirtualKey.OEM_PLUS },
        { DoomKey.LBracket, VirtualKey.OEM_4 }, { DoomKey.RBracket, VirtualKey.OEM_6 },
        { DoomKey.Semicolon, VirtualKey.OEM_1 }, { DoomKey.Quote, VirtualKey.OEM_7 },
        { DoomKey.Comma, VirtualKey.OEM_COMMA }, { DoomKey.Period, VirtualKey.OEM_PERIOD },
        { DoomKey.Slash, VirtualKey.OEM_2 }, { DoomKey.Backslash, VirtualKey.OEM_5 },
        { DoomKey.Tilde, VirtualKey.OEM_3 },

        { DoomKey.Add, VirtualKey.ADD }, { DoomKey.Subtract, VirtualKey.SUBTRACT },
        { DoomKey.Multiply, VirtualKey.MULTIPLY }, { DoomKey.Divide, VirtualKey.DIVIDE },

        { DoomKey.F1, VirtualKey.F1 }, { DoomKey.F2, VirtualKey.F2 }, { DoomKey.F3, VirtualKey.F3 },
        { DoomKey.F4, VirtualKey.F4 }, { DoomKey.F5, VirtualKey.F5 }, { DoomKey.F6, VirtualKey.F6 },
        { DoomKey.F7, VirtualKey.F7 }, { DoomKey.F8, VirtualKey.F8 }, { DoomKey.F9, VirtualKey.F9 },
        { DoomKey.F10, VirtualKey.F10 }, { DoomKey.F11, VirtualKey.F11 }, { DoomKey.F12, VirtualKey.F12 },
    };

    private readonly object gate = new();
    private readonly bool[] keys = new bool[(int)DoomKey.Count];
    private bool mouse1;
    private bool mouse2;
    private float deltaX;
    private float deltaY;

    public void Capture(IKeyState keyState, Vector2 mouseDelta, bool mouseLookActive)
    {
        lock (this.gate)
        {
            foreach (var pair in Map)
            {
                var down = false;
                try
                {
                    if (keyState.IsVirtualKeyValid((int)pair.Value))
                    {
                        down = keyState[pair.Value];
                    }
                }
                catch
                {
                    down = false;
                }

                this.keys[(int)pair.Key] = down;
            }

            this.mouse1 = IsDown(keyState, VirtualKey.LBUTTON);
            this.mouse2 = IsDown(keyState, VirtualKey.RBUTTON);

            if (mouseLookActive)
            {
                this.deltaX += mouseDelta.X;
                this.deltaY += mouseDelta.Y;
            }
        }
    }

    private static bool IsDown(IKeyState keyState, VirtualKey key)
    {
        try
        {
            return keyState.IsVirtualKeyValid((int)key) && keyState[key];
        }
        catch
        {
            return false;
        }
    }

    public bool[] SnapshotKeys()
    {
        lock (this.gate)
        {
            return (bool[])this.keys.Clone();
        }
    }

    public bool IsMouseButtonPressed(DoomMouseButton button)
    {
        lock (this.gate)
        {
            return button switch
            {
                DoomMouseButton.Mouse1 => this.mouse1,
                DoomMouseButton.Mouse2 => this.mouse2,
                _ => false,
            };
        }
    }

    public Vector2 TakeMouseDelta()
    {
        lock (this.gate)
        {
            var delta = new Vector2(this.deltaX, this.deltaY);
            this.deltaX = 0;
            this.deltaY = 0;
            return delta;
        }
    }

    public void ClearTransient()
    {
        lock (this.gate)
        {
            this.deltaX = 0;
            this.deltaY = 0;
        }
    }
}