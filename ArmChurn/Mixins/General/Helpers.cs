using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        public double WrapDiff(double before, double current, double wrap)
        {
            var midpoint = wrap / 2;
            return (current - before + midpoint + wrap) % wrap - midpoint;
        }

        public double RadianDiff(float before, float after) => WrapDiff((double)before, (double)after, Math.PI * 2.0);

        public float AngleDiff(float before, float after) => (float)WrapDiff((double)before, (double)after, 360.0);

        public float RadiansToDegrees(float radians) => radians * 180.0f / (float)Math.PI;

        public float DegreesToRadians(float degrees) => degrees / 180.0f * (float)Math.PI;

        public int GetBlocksOrGroup<T>(string name, List<T> into)
            where T : class, IMyTerminalBlock
        {
            into.Clear();
            if (string.IsNullOrWhiteSpace(name))
                return 0;

            // Is it a group?
            var group = GridTerminalSystem.GetBlockGroupWithName(name);
            if (group != null)
            {
                group.GetBlocksOfType(into);
            }
            else
            {
                if (blocks_ == null)
                    blocks_ = new List<IMyTerminalBlock>();
                else
                    blocks_.Clear();

                GridTerminalSystem.SearchBlocksOfName(name, blocks_, block => block is T);
                foreach (var block in blocks_)
                {
                    T tblock = (T)block;
                    if (tblock != null)
                        into.Add(tblock);
                }
            }

            return into.Count;
        }

        public void EnableBlocks<T>(List<T> blocks, bool enabled)
            where T : class, IMyFunctionalBlock
        {
            for (int i = 0; i < blocks.Count; ++i)
            {
                blocks[i].Enabled = enabled;
            }
        }

        public void ListBlocks<T>(List<T> blocks)
            where T : class, IMyTerminalBlock
        {
            List<string> names = new List<string>();
            foreach (var block in blocks)
            {
                names.Add(((IMyTerminalBlock)block).DisplayNameText);
            }
            Echo(string.Join(", ", names));
        }
    }
}
