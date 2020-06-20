using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRage;
using VRageMath;
using System.Runtime.CompilerServices;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        private readonly Config config_;

        List<IMyTerminalBlock> blocks_ = new List<IMyTerminalBlock>();

        List<IMyShipDrill> drills_ = new List<IMyShipDrill>();
        List<IMyPistonBase> pistons_ = new List<IMyPistonBase>();
        List<IMyMotorStator> rotors_ = new List<IMyMotorStator>();
        float direction_;
        int rotations_;
        int ticks_;
        float depth_;

        IEnumerator<bool> running_ = null;

        private string defaultConfig_ = @"
drills=*Drills
pistons=*Pistons
rotors=*Rotors

rotate_for=360
rotate_rpm=0.6

descent_step=0.5
descent_rate=0.1
            ";

        public Program()
        {
            config_ = new Config(defaultConfig_);
        }

        public void Save()
        {
            // Called when the program needs to save its state. Use
            // this method to save your state to the Storage field
            // or some other means. 
            // 
            // This method is optional and can be removed if not
            // needed.
        }

        private void Refresh()
        {
            config_.Parse(defaultConfig_);
            config_.Parse(Me.CustomData);

            if (GetBlocksOrGroup(config_["drills"], drills_) < 1)
                throw new Exception($"Could not find '{config_["drills"]}'");
            if (GetBlocksOrGroup(config_["pistons"], pistons_) < 1)
                throw new Exception($"Could not find '{config_["pistons"]}'");
            if (GetBlocksOrGroup(config_["rotors"], rotors_) < 1)
                throw new Exception($"Could not find '{config_["rotors"]}'");

            if (rotors_.Count != 1)
                throw new Exception("Invalid rotor count.");
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (updateSource == UpdateType.Terminal)
            {
                if (running_ != null)
                    throw new Exception("Already running.");

                Refresh();

                direction_ = 1.0f;
                rotations_ = 0;
                ticks_ = 0;
                depth_ = 0.0f;

                running_ = StartState();
                Runtime.UpdateFrequency |= UpdateFrequency.Update10;
            }

            if (running_ != null)
            {
                var state = running_;
                ++ticks_;
                Echo($"Rotation: {rotations_}, Tick: {ticks_}");
                Echo($"Rotor: {rotors_[0].DisplayNameText}");
                Echo($"Depth: {depth_}");

                bool continuing = state.MoveNext();
                if (running_ != state)
                {
                    Echo("Changed state");
                    state.Dispose();
                }
                if (!continuing || running_ == null)
                {
                    Echo("Ended");
                    running_ = null;
                    Runtime.UpdateFrequency &= ~UpdateFrequency.Update10;
                }
                else
                {
                    char[] spinner = new char[4] { '|', '/', '-', '\\' };
                    Echo($"{spinner[ticks_ % 4]}");
                }
            }
        }

        private void NextState(IEnumerator<bool> state)
        {
            running_ = state;
        }

        private IMyPistonBase PendingPiston()
        {
            foreach (var piston in pistons_)
            {
                if (piston.CurrentPosition < piston.MaxLimit)
                    return piston;
            }
            return null;
        }

        private IEnumerator<bool> StartState()
        {
            Refresh();

            EnableBlocks(pistons_, false);
            EnableBlocks(drills_, false);
            foreach (var rotor in rotors_)
            {
                rotor.TargetVelocityRPM = 0.0f;
                rotor.RotorLock = true;
            }

            if (PendingPiston() == null)
            {
                Echo("No pending pistons");
                yield break;
            }

            ++rotations_;

            EnableBlocks(drills_, true);
            NextState(RotateState());
            yield return true;
        }

        private IEnumerator<bool> RotateState()
        {
            float travelled = 0.0f;
            float lastAngle = RadiansToDegrees(rotors_[0].Angle);

            // Value is in radians, so a complete circle is 2.0rad
            float goal = config_.AsFloat("rotate_for");
            float rpm = direction_ * config_.AsFloat("rotate_rpm");
            while (Math.Abs(travelled) < goal)
            {
                Echo("Rotating");
                Echo($"{goal - Math.Abs(travelled)} degrees to go at {rpm}");
                rotors_[0].TargetVelocityRPM = rpm;
                rotors_[0].RotorLock = false;

                yield return true;

                var angle = RadiansToDegrees(rotors_[0].Angle);
                travelled += AngleDiff(lastAngle, angle);
                lastAngle = angle;
            }

            rotors_[0].RotorLock = true;
            direction_ *= -1.0f;

            Echo("Lowering");
            NextState(LowerState());

            yield return true;
        }

        private IEnumerator<bool> LowerState()
        {
            // Find a piston that's not max'd and lower it.
            var piston = PendingPiston();
            if (piston == null)
            {
                Echo("No pending pistons left.");
                yield break;
            }

            var min_float = 0.0001f;
            var descent_step = Math.Max(config_.AsFloat("descent_step"), min_float);
            var descent_rate = Math.Max(config_.AsFloat("descent_rate"), min_float);
            var start = piston.CurrentPosition;
            var goal = Math.Min(start + descent_step, piston.MaxLimit);
            while (piston.CurrentPosition < goal)
            {
                var remaining = goal - piston.CurrentPosition;
                var maxVelocity = Math.Min(remaining / 0.3f, descent_rate);
                piston.Velocity = Math.Max(maxVelocity, min_float);
                piston.Enabled = true;
                Echo("Lowering");
                Echo($"{remaining}m to {goal} @ {piston.Velocity}m/s");
                Runtime.UpdateFrequency |= UpdateFrequency.Update1;

                yield return true;
            }

            piston.Velocity = 0.0f;
            piston.Enabled = false;

            Runtime.UpdateFrequency &= ~UpdateFrequency.Update1;

            depth_ += piston.CurrentPosition - start;

            NextState(StartState());

            yield return true;
        }
    }
}
