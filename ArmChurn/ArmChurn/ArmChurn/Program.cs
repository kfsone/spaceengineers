using Sandbox.Game;
using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.CompilerServices;
using VRage.Game.ModAPI.Ingame;
using VRage.Game;
using EmptyKeys.UserInterface.Generated.DataTemplatesContracts_Bindings;
using System.ComponentModel.Design.Serialization;
using System.Configuration;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        const string DefaultConfig = @"""
# Name that matches one or more [advanced] rotor or a group containing it/them.
rotors=*Rotors
# Name that matches pistons/groups that control the mine 'depth'. If there is more
# than one, these are handled one after the other.
shaft=*Shaft
# Name that matches one or more pistons/groups that control the mine 'width'. If
# is more than one, they are extended/retracted together.
arms=*Arms
# Name that matches one or more drills.
drills=*Drills

# How many degrees does the rotor need to rotate before adjusting pistons? For one arm,
# this should be 360, if you have two arms in facing opposite each other, 180, etc.
rotation=360.0
# How many degress per second the rotor should do.
rotors_dps=5.0

# How far the shaft should extend before rotating again, in meters.
shaft_step=0.5
# How fast you want the shaft pistons to operate, in meters/second.
shaft_mps=0.2

# How far the arms should extend before rotating again, in meters.
arms_step=0.5
# How fast you want the arm pistons to operate, in meters/second.
arms_mps=0.2

# After this many rotation ticks, run the rotor backwards for a tick.
# Any value < 3 disables.
stagger=0
""";
        // General scratch list, to avoid reallocations.
        private List<IMyTerminalBlock> blocks_ = new List<IMyTerminalBlock>();

        private List<IMyMotorStator> rotors_ = new List<IMyMotorStator>();
        private List<IMyPistonBase> shaft_ = new List<IMyPistonBase>();
        private List<IMyPistonBase> arms_ = new List<IMyPistonBase>();
        private List<IMyShipDrill> drills_ = new List<IMyShipDrill>();

        private IEnumerator<bool> running_ = null;

        private readonly Config config_;

        // This file contains your actual script.
        //
        // You can either keep all your code here, or you can create separate
        // code files to make your program easier to navigate while coding.
        //
        // In order to add a new utility class, right-click on your project, 
        // select 'New' then 'Add Item...'. Now find the 'Space Engineers'
        // category under 'Visual C# Items' on the left hand side, and select
        // 'Utility Class' in the main area. Name it in the box below, and
        // press OK. This utility class will be merged in with your code when
        // deploying your final script.
        //
        // You can also simply create a new utility class manually, you don't
        // have to use the template if you don't want to. Just do so the first
        // time to see what a utility class looks like.
        // 
        // Go to:
        // https://github.com/malware-dev/MDK-SE/wiki/Quick-Introduction-to-Space-Engineers-Ingame-Scripts
        //
        // to learn more about ingame scripts.

        public Program()
        {
            config_ = new Config(DefaultConfig);
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

        private void ExecuteCommand(string argument)
        {
            switch (argument.Trim())
            {
                case "abort":
                    Halt();
                    return;
                case "halt":
                    Halt();
                    return;
                case "stop":
                    Halt();
                    return;

                case "config":
                    Configure();

                    Echo($"Rotors [{config_["rotors"]}]");
                    ListBlocks(rotors_);
                    Echo($"Shaft Pistons [{config_["shaft"]}]");
                    ListBlocks(shaft_);
                    Echo($"Arms [{config_["arms"]}]");
                    ListBlocks(arms_);
                    Echo($"Drills [{config_["drills"]}]");
                    ListBlocks(drills_);

                    break;

                default:
                    Echo("Unknown command: {argument}");
                    break;
            }
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if (argument != "")
            {
                ExecuteCommand(argument);
                return;
            }

            if (updateSource == UpdateType.Terminal)
            {
                if (running_ != null)
                {
                    Echo($"Already running.");
                    return;
                }

                StopAll();
                running_ = StateMachine();
                Echo("Starting");
            }
            if (running_ == null)
            {
                Echo("Nothing running.");
                Runtime.UpdateFrequency = UpdateFrequency.None;
                return;
            }

            if (running_.MoveNext() == false)
                Halt();
            else
            {
                Runtime.UpdateFrequency |= UpdateFrequency.Update100;
            }
        }

        private bool Configure()
        {
            config_.Parse(Me.CustomData);
            if (GetBlocksOrGroup(config_["rotors"], rotors_) < 1)
                return Abort("No rotors defined.");
            if (GetBlocksOrGroup(config_["shaft"], shaft_) < 1)
                return Abort("No shaft pistons defined.");
            if (GetBlocksOrGroup(config_["arms"], arms_) < 1)
                return Abort("No arm pistons defined.");
            if (GetBlocksOrGroup(config_["drills"], drills_) < 1)
                return Abort("No drills defined.");

            return true;
        }


        public void StopAll()
        {
            EnableBlocks(drills_, false);
            EnableBlocks(arms_, false);
            EnableBlocks(shaft_, false);
            for (var i = 0; i < rotors_.Count; ++i)
            {
                rotors_[i].RotorLock = true;
            }
        }

        public void Halt()
        {
            if (running_ != null)
                running_.Dispose();
            running_ = null;
            Runtime.UpdateFrequency &= ~UpdateFrequency.Update100;
            StopAll();
            Echo("Halted.");
        }

        private bool Abort(string reason)
        {
            Echo($"Abort: {reason}");
            return false;
        }

        public static float DegToRad(float deg) => deg / 180.0f * (float)Math.PI;
        public static float RadToDeg(float rad) => rad * 180.0f / (float)Math.PI;

        private IEnumerator<bool> RotationCycle()
        {
            List<long> rotors = new List<long>();
            List<float> lastPositions = new List<float>();
            List<float> travelled = new List<float>();

            const float min_rate = 0.0001f;
            float rotation = RadiansToDegrees(Math.Abs(config_.AsFloat("rotation")));
            float rate_dps = Math.Max(Math.Min(Math.Abs(config_.AsFloat("rotors_dps")), 30.0f), min_rate);
            int stagger = config_.AsInt("stagger");
            if (stagger < 3) stagger = -1;

            Echo($"Rotation {rotors_.Count} rotor(s) {rotation}deg");
            for (var i = 0; i < rotors_.Count; ++i)
            {
                var block = rotors_[i];
                rotors.Add(block.EntityId);
                lastPositions.Add(RadToDeg(block.Angle));
                travelled.Add(0.0f);
                block.TargetVelocityRad = DegToRad(Math.Min(0.01f, rate_dps));
                block.RotorLock = false;
                block.Enabled = true;
            }

            int tick = 0;
            int pending = rotors.Count;
            while (pending > 0)
            {
                yield return true;

                ++tick;
                pending = rotors.Count;

                var summary = "";
                for (int i = 0; i < rotors.Count; ++i)
                {
                    var rotor = (IMyMotorStator)GridTerminalSystem.GetBlockWithId(rotors[i]);
                    if (rotor == null || !rotor.Enabled || rotor.RotorLock)
                    {
                        summary += $"rotor{i+1}:ready ";
                        pending--;
                        continue;
                    }

                    var position = RadToDeg(rotor.Angle);
                    var delta = position - lastPositions[i];
                    if (Math.Abs(delta) > 180.0f)
                        delta = (delta + 360.0f) % 360.0f;
                    travelled[i] += delta;
                    lastPositions[i] = position;
                    var remaining = rotation - travelled[i];

                    if (remaining < 0)
                    {
                        summary += $"rotor{i+1}:set ";
                        rotor.RotorLock = true;
                        rotor.TargetVelocityRad = 0.0f;
                        continue;
                    }

                    var deg_p_s = Math.Min(remaining / 2, rate_dps);
                    if (stagger >= 3 && (tick % stagger) == 0)
                        deg_p_s = -deg_p_s * 0.6f;
                    rotor.TargetVelocityRad = DegToRad(deg_p_s);
                    summary += $"rotor{i+1}:{(int)(travelled[i] * 100.0 / rotation)}%:{deg_p_s}d/s ";
                }

                Echo($"Rotation {tick} {summary}");
            }

            yield return true;
            // Back the rotors up a tiny amount.
            Echo("Reversing rotors");
            for (var i = 0; i < rotors.Count; ++i)
            {
                var rotor = (IMyMotorStator)GridTerminalSystem.GetBlockWithId(rotors[i]);
                if (rotor== null || !rotor.Enabled)
                    continue;
                rotor.TargetVelocityRad = -0.01f;
                rotor.RotorLock = false;
            }

            yield return true;
            Echo("Parking rotors");
            for (var i = 0; i < rotors.Count; ++i)
            {
                var rotor = (IMyMotorStator)GridTerminalSystem.GetBlockWithId(rotors[i]);
                if (rotor == null || !rotor.Enabled)
                    continue;
                rotor.TargetVelocityRad = 0.0f;
                rotor.RotorLock = true;
            }
        }

        static public int GetStepNo(float minPos, float currentPos, float rate)
        {
            return (int)((currentPos - minPos) / rate);
        }

        static public float GetStepPosition(float minPos, float rate, int stepNo, float maxPos)
        {
            return Math.Min(minPos + (rate * stepNo), maxPos);
        }

        private IEnumerator<bool> ShaftCycle(Func<IEnumerator<bool>> innerCycle)
        {
            List<long> pistons = new List<long>();

            const float min_rate = 0.0001f;
            float rate = Math.Max(Math.Min(Math.Abs(config_.AsFloat("shaft_mps")), 5.0f), min_rate);
            float stepping = Math.Max(config_.AsFloat("shaft_step"), 0.01f);

            for (var i = 0; i < shaft_.Count; ++i)
            {
                var piston = shaft_[i];
                pistons.Add(piston.EntityId);
                piston.Velocity = 0.0f;
                piston.Enabled = false;
            }

            for (int i = 0; i < pistons.Count; ++i)
            {
                var piston = (IMyPistonBase)GridTerminalSystem.GetBlockWithId(pistons[i]);
                if (piston == null || piston.CurrentPosition >= piston.MaxLimit)
                    continue;

                int steps = GetStepNo(piston.MinLimit, piston.MaxLimit, stepping);
                while (true)
                {
                    int stepNo = GetStepNo(piston.MinLimit, piston.CurrentPosition, stepping);
                    // Do a rotation of the drills
                    var inner = innerCycle();
                    while (true)
                    {
                        Echo($"{piston.DisplayNameText} #{stepNo} @ {piston.CurrentPosition}m");
                        bool continuing = inner.MoveNext();
                        if (!continuing)
                            break;
                        yield return true;
                    }
                    inner.Dispose();

                    if (piston.CurrentPosition >= piston.MaxLimit)
                        break;

                    // Advance down to the next step height
                    ++stepNo;
                    float endPosition = GetStepPosition(piston.MinLimit, stepping, stepNo, piston.MaxLimit);
                    while (piston.CurrentPosition < endPosition)
                    {
                        Echo($"SP{i + 1} moving to #{stepNo} : {piston.CurrentPosition} -> {endPosition}");
                        Echo($"Stepping is {stepping}, min = {piston.MinLimit}, max = {piston.MaxLimit}");
                        piston.Velocity = Math.Min(endPosition - piston.CurrentPosition / 2, rate);
                        piston.Enabled = true;
                        yield return true;
                    }

                    piston.Enabled = false;
                    Echo($"SP{i + 1} ready.");
                    yield return true;
                }
            }

            // TODO: Should retract the arms.
            while (true)
            {
                yield return true;
                int pending = 0;
                for (var i = 0; i < pistons.Count; ++i)
                {
                    var piston = (IMyPistonBase)GridTerminalSystem.GetBlockWithId(pistons[i]);
                    if (piston == null)
                        continue;
                    if (piston.CurrentPosition <= piston.MinLimit)
                    {
                        piston.Enabled = false;
                        continue;
                    }
                    pending += 1;
                    piston.Enabled = true;
                    piston.Velocity = Math.Max(-1.0f, piston.CurrentPosition - piston.MinLimit / 2);
                }

                if (pending == 0)
                    break;

                Echo($"Resetting {pending} shaft pistons.");
                yield return true;
            }
        }

        //private IEnumerator<bool> ArmCycle(IEnumerator<bool> innerCycle)
        //{

        //}

        private IEnumerator<bool> StateMachine()
        {
            if (Configure())
            {
                EnableBlocks(drills_, true);

                var cycle = ShaftCycle(RotationCycle);
                while (cycle.MoveNext())
                {
                    yield return true;
                }

                EnableBlocks(drills_, false);

                Echo("Done");
            }
        }
    }
}
