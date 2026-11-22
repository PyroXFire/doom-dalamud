//
// Copyright (C) 1993-1996 Id Software, Inc.
// Copyright (C) 2019-2020 Nobuaki Tanaka
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//

using System;
using System.Numerics;

namespace ManagedDoom.UserInput
{
    /// <summary>
    /// IUserInput implementation that polls an external key/mouse state source instead of
    /// receiving window events. Used when the engine is hosted inside another application
    /// (e.g. a Dalamud plugin) that owns the input pipeline.
    /// </summary>
    public sealed class PolledUserInput : IUserInput
    {
        private readonly Config config;
        private readonly Func<DoomKey, bool> isKeyPressed;
        private readonly Func<DoomMouseButton, bool> isMouseButtonPressed;
        private readonly Func<Vector2> takeMouseDelta;

        private bool mouseGrabbed;
        private int turnHeld;
        private int mouseSensitivity;
        private readonly bool[] weaponKeys;

        public PolledUserInput(
            Config config,
            Func<DoomKey, bool> isKeyPressed,
            Func<DoomMouseButton, bool> isMouseButtonPressed,
            Func<Vector2> takeMouseDelta,
            bool useMouse = true)
        {
            this.config = config;
            this.isKeyPressed = isKeyPressed;
            this.isMouseButtonPressed = isMouseButtonPressed;
            this.takeMouseDelta = takeMouseDelta;
            this.useMouse = useMouse;

            this.turnHeld = 0;
            this.mouseSensitivity = config.mouse_sensitivity;
            this.weaponKeys = new bool[7];
        }

        private readonly bool useMouse;

        public void BuildTicCmd(TicCmd cmd)
        {
            var keyForward = IsPressed(config.key_forward);
            var keyBackward = IsPressed(config.key_backward);
            var keyStrafeLeft = IsPressed(config.key_strafeleft);
            var keyStrafeRight = IsPressed(config.key_straferight);
            var keyTurnLeft = IsPressed(config.key_turnleft);
            var keyTurnRight = IsPressed(config.key_turnright);
            var keyFire = IsPressed(config.key_fire);
            var keyUse = IsPressed(config.key_use);
            var keyRun = IsPressed(config.key_run);
            var keyStrafe = IsPressed(config.key_strafe);

            weaponKeys[0] = isKeyPressed(DoomKey.Num1);
            weaponKeys[1] = isKeyPressed(DoomKey.Num2);
            weaponKeys[2] = isKeyPressed(DoomKey.Num3);
            weaponKeys[3] = isKeyPressed(DoomKey.Num4);
            weaponKeys[4] = isKeyPressed(DoomKey.Num5);
            weaponKeys[5] = isKeyPressed(DoomKey.Num6);
            weaponKeys[6] = isKeyPressed(DoomKey.Num7);

            cmd.Clear();

            var strafe = keyStrafe;
            var speed = keyRun ? 1 : 0;
            var forward = 0;
            var side = 0;

            if (config.game_alwaysrun)
            {
                speed = 1 - speed;
            }

            if (keyTurnLeft || keyTurnRight)
            {
                turnHeld++;
            }
            else
            {
                turnHeld = 0;
            }

            int turnSpeed;
            if (turnHeld < PlayerBehavior.SlowTurnTics)
            {
                turnSpeed = 2;
            }
            else
            {
                turnSpeed = speed;
            }

            if (strafe)
            {
                if (keyTurnRight)
                {
                    side += PlayerBehavior.SideMove[speed];
                }
                if (keyTurnLeft)
                {
                    side -= PlayerBehavior.SideMove[speed];
                }
            }
            else
            {
                if (keyTurnRight)
                {
                    cmd.AngleTurn -= (short)PlayerBehavior.AngleTurn[turnSpeed];
                }
                if (keyTurnLeft)
                {
                    cmd.AngleTurn += (short)PlayerBehavior.AngleTurn[turnSpeed];
                }
            }

            if (keyForward)
            {
                forward += PlayerBehavior.ForwardMove[speed];
            }
            if (keyBackward)
            {
                forward -= PlayerBehavior.ForwardMove[speed];
            }

            if (keyStrafeLeft)
            {
                side -= PlayerBehavior.SideMove[speed];
            }
            if (keyStrafeRight)
            {
                side += PlayerBehavior.SideMove[speed];
            }

            if (keyFire)
            {
                cmd.Buttons |= TicCmdButtons.Attack;
            }

            if (keyUse)
            {
                cmd.Buttons |= TicCmdButtons.Use;
            }

            // Check weapon keys.
            for (var i = 0; i < weaponKeys.Length; i++)
            {
                if (weaponKeys[i])
                {
                    cmd.Buttons |= TicCmdButtons.Change;
                    cmd.Buttons |= (byte)(i << TicCmdButtons.WeaponShift);
                    break;
                }
            }

            if (useMouse && mouseGrabbed)
            {
                var delta = takeMouseDelta();
                var ms = 0.5F * config.mouse_sensitivity;
                var mx = (int)MathF.Round(ms * delta.X);
                var my = (int)MathF.Round(ms * -delta.Y);
                forward += my;
                if (strafe)
                {
                    side += mx * 2;
                }
                else
                {
                    cmd.AngleTurn -= (short)(mx * 0x8);
                }
            }

            if (forward > PlayerBehavior.MaxMove)
            {
                forward = PlayerBehavior.MaxMove;
            }
            else if (forward < -PlayerBehavior.MaxMove)
            {
                forward = -PlayerBehavior.MaxMove;
            }

            if (side > PlayerBehavior.MaxMove)
            {
                side = PlayerBehavior.MaxMove;
            }
            else if (side < -PlayerBehavior.MaxMove)
            {
                side = -PlayerBehavior.MaxMove;
            }

            cmd.ForwardMove += (sbyte)forward;
            cmd.SideMove += (sbyte)side;
        }

        private bool IsPressed(KeyBinding keyBinding)
        {
            foreach (var key in keyBinding.Keys)
            {
                if (isKeyPressed(key))
                {
                    return true;
                }
            }

            if (mouseGrabbed)
            {
                foreach (var mouseButton in keyBinding.MouseButtons)
                {
                    if (isMouseButtonPressed(mouseButton))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public void Reset()
        {
        }

        public void GrabMouse()
        {
            if (!useMouse)
            {
                return;
            }

            mouseGrabbed = true;
        }

        public void ReleaseMouse()
        {
            mouseGrabbed = false;
        }

        public int MaxMouseSensitivity => 9;

        public int MouseSensitivity
        {
            get => mouseSensitivity;
            set => mouseSensitivity = value;
        }
    }
}