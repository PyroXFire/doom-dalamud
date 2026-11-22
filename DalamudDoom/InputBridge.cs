using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Plugin.Services;

using ManagedDoom;

namespace DalamudDoom;

/// <summary>
/// Snapshots keyboard (via ImGui, which Dalamud feeds from the window procedure before the game
/// acts on it), mouse, and the game's own gamepad state into a form the engine thread can poll.
/// </summary>
public sealed class InputBridge
{
    private static readonly Dictionary<DoomKey, ImGuiKey> KeyMap = new()
    {
        { DoomKey.A, ImGuiKey.A }, { DoomKey.B, ImGuiKey.B }, { DoomKey.C, ImGuiKey.C },
        { DoomKey.D, ImGuiKey.D }, { DoomKey.E, ImGuiKey.E }, { DoomKey.F, ImGuiKey.F },
        { DoomKey.G, ImGuiKey.G }, { DoomKey.H, ImGuiKey.H }, { DoomKey.I, ImGuiKey.I },
        { DoomKey.J, ImGuiKey.J }, { DoomKey.K, ImGuiKey.K }, { DoomKey.L, ImGuiKey.L },
        { DoomKey.M, ImGuiKey.M }, { DoomKey.N, ImGuiKey.N }, { DoomKey.O, ImGuiKey.O },
        { DoomKey.P, ImGuiKey.P }, { DoomKey.Q, ImGuiKey.Q }, { DoomKey.R, ImGuiKey.R },
        { DoomKey.S, ImGuiKey.S }, { DoomKey.T, ImGuiKey.T }, { DoomKey.U, ImGuiKey.U },
        { DoomKey.V, ImGuiKey.V }, { DoomKey.W, ImGuiKey.W }, { DoomKey.X, ImGuiKey.X },
        { DoomKey.Y, ImGuiKey.Y }, { DoomKey.Z, ImGuiKey.Z },

        { DoomKey.Num0, ImGuiKey.Key0 }, { DoomKey.Num1, ImGuiKey.Key1 },
        { DoomKey.Num2, ImGuiKey.Key2 }, { DoomKey.Num3, ImGuiKey.Key3 },
        { DoomKey.Num4, ImGuiKey.Key4 }, { DoomKey.Num5, ImGuiKey.Key5 },
        { DoomKey.Num6, ImGuiKey.Key6 }, { DoomKey.Num7, ImGuiKey.Key7 },
        { DoomKey.Num8, ImGuiKey.Key8 }, { DoomKey.Num9, ImGuiKey.Key9 },

        { DoomKey.Numpad0, ImGuiKey.Keypad0 }, { DoomKey.Numpad1, ImGuiKey.Keypad1 },
        { DoomKey.Numpad2, ImGuiKey.Keypad2 }, { DoomKey.Numpad3, ImGuiKey.Keypad3 },
        { DoomKey.Numpad4, ImGuiKey.Keypad4 }, { DoomKey.Numpad5, ImGuiKey.Keypad5 },
        { DoomKey.Numpad6, ImGuiKey.Keypad6 }, { DoomKey.Numpad7, ImGuiKey.Keypad7 },
        { DoomKey.Numpad8, ImGuiKey.Keypad8 }, { DoomKey.Numpad9, ImGuiKey.Keypad9 },

        { DoomKey.Left, ImGuiKey.LeftArrow }, { DoomKey.Right, ImGuiKey.RightArrow },
        { DoomKey.Up, ImGuiKey.UpArrow }, { DoomKey.Down, ImGuiKey.DownArrow },

        { DoomKey.Escape, ImGuiKey.Escape }, { DoomKey.Space, ImGuiKey.Space },
        { DoomKey.Enter, ImGuiKey.Enter }, { DoomKey.Backspace, ImGuiKey.Backspace },
        { DoomKey.Tab, ImGuiKey.Tab }, { DoomKey.Pause, ImGuiKey.Pause },

        { DoomKey.LShift, ImGuiKey.LeftShift }, { DoomKey.RShift, ImGuiKey.RightShift },
        { DoomKey.LControl, ImGuiKey.LeftCtrl }, { DoomKey.RControl, ImGuiKey.RightCtrl },
        { DoomKey.LAlt, ImGuiKey.LeftAlt }, { DoomKey.RAlt, ImGuiKey.RightAlt },
        { DoomKey.LSystem, ImGuiKey.LeftSuper }, { DoomKey.RSystem, ImGuiKey.RightSuper },
        { DoomKey.Menu, ImGuiKey.Menu },

        { DoomKey.PageUp, ImGuiKey.PageUp }, { DoomKey.PageDown, ImGuiKey.PageDown },
        { DoomKey.Home, ImGuiKey.Home }, { DoomKey.End, ImGuiKey.End },
        { DoomKey.Insert, ImGuiKey.Insert }, { DoomKey.Delete, ImGuiKey.Delete },

        { DoomKey.Hyphen, ImGuiKey.Minus }, { DoomKey.Equal, ImGuiKey.Equal },
        { DoomKey.LBracket, ImGuiKey.LeftBracket }, { DoomKey.RBracket, ImGuiKey.RightBracket },
        { DoomKey.Semicolon, ImGuiKey.Semicolon }, { DoomKey.Quote, ImGuiKey.Apostrophe },
        { DoomKey.Comma, ImGuiKey.Comma }, { DoomKey.Period, ImGuiKey.Period },
        { DoomKey.Slash, ImGuiKey.Slash }, { DoomKey.Backslash, ImGuiKey.Backslash },
        { DoomKey.Tilde, ImGuiKey.GraveAccent },

        { DoomKey.Add, ImGuiKey.KeypadAdd }, { DoomKey.Subtract, ImGuiKey.KeypadSubtract },
        { DoomKey.Multiply, ImGuiKey.KeypadMultiply }, { DoomKey.Divide, ImGuiKey.KeypadDivide },

        { DoomKey.F1, ImGuiKey.F1 }, { DoomKey.F2, ImGuiKey.F2 }, { DoomKey.F3, ImGuiKey.F3 },
        { DoomKey.F4, ImGuiKey.F4 }, { DoomKey.F5, ImGuiKey.F5 }, { DoomKey.F6, ImGuiKey.F6 },
        { DoomKey.F7, ImGuiKey.F7 }, { DoomKey.F8, ImGuiKey.F8 }, { DoomKey.F9, ImGuiKey.F9 },
        { DoomKey.F10, ImGuiKey.F10 }, { DoomKey.F11, ImGuiKey.F11 }, { DoomKey.F12, ImGuiKey.F12 },
    };

    private readonly object gate = new();
    private readonly bool[] keys = new bool[(int)DoomKey.Count];
    private readonly bool[] scratch = new bool[(int)DoomKey.Count];
    private bool mouse1;
    private bool mouse2;
    private float deltaX;
    private float deltaY;
    private float moveX;
    private float moveY;
    private float turn;

    public void Capture(
        IGamepadState? gamepad,
        Vector2 mouseDelta,
        bool mouseLookActive,
        bool controllerEnabled,
        float deadzone)
    {
        Array.Clear(this.scratch);
        foreach (var pair in KeyMap)
        {
            this.scratch[(int)pair.Key] = ImGui.IsKeyDown(pair.Value);
        }

        var left = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        var right = ImGui.IsMouseDown(ImGuiMouseButton.Right);

        float stickMoveX = 0f, stickMoveY = 0f, stickTurn = 0f;

        if (controllerEnabled && gamepad != null)
        {
            var move = ApplyDeadzone(gamepad.LeftStick, deadzone);
            stickMoveX = move.X;
            stickMoveY = move.Y;

            // Pushing right turns right; same sign convention as mouse look.
            stickTurn = -ApplyDeadzone(new Vector2(gamepad.RightStick.X, 0f), deadzone).X;

            // Piggyback the pad onto the existing key bindings so fire/use behave like the keyboard.
            if (gamepad.Raw(GamepadButtons.R2) > 0.5f || gamepad.Raw(GamepadButtons.South) > 0.5f)
            {
                this.scratch[(int)DoomKey.LControl] = true;
            }

            if (gamepad.Raw(GamepadButtons.West) > 0.5f)
            {
                this.scratch[(int)DoomKey.Space] = true;
            }
        }

        lock (this.gate)
        {
            Array.Copy(this.scratch, this.keys, this.keys.Length);
            this.mouse1 = left;
            this.mouse2 = right;
            this.moveX = stickMoveX;
            this.moveY = stickMoveY;
            this.turn = stickTurn;

            if (mouseLookActive)
            {
                this.deltaX += mouseDelta.X;
                this.deltaY += mouseDelta.Y;
            }
        }
    }

    private static Vector2 ApplyDeadzone(Vector2 stick, float deadzone)
    {
        var magnitude = stick.Length();
        if (magnitude <= deadzone || magnitude <= 0f)
        {
            return Vector2.Zero;
        }

        var scaled = MathF.Min(1f, (magnitude - deadzone) / MathF.Max(0.01f, 1f - deadzone));
        return stick / magnitude * scaled;
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

    public Vector2 MoveAxes()
    {
        lock (this.gate)
        {
            return new Vector2(this.moveX, this.moveY);
        }
    }

    public float TurnAxis()
    {
        lock (this.gate)
        {
            return this.turn;
        }
    }

    public void ClearTransient()
    {
        lock (this.gate)
        {
            this.deltaX = 0;
            this.deltaY = 0;
            this.moveX = 0;
            this.moveY = 0;
            this.turn = 0;
        }
    }
}