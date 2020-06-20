using Malware.MDKUtilities;
using System.Diagnostics;
using System;

namespace IngameScript.MDK
{
    public class TestBootstrapper
    {
        // All the files in this folder, as well as all files containing the file ".debug.", will be excluded
        // from the build process. You can use this to create utilites for testing your scripts directly in 
        // Visual Studio.

        static TestBootstrapper()
        {
            // Initialize the MDK utility framework
            MDKUtilityFramework.Load();
        }

        public static void Main()
        {
            // In order for your program to actually run, you will need to provide a mockup of all the facilities 
            // your script uses from the game, since they're not available outside of the game.

            // Create and configure the desired program.
            var program = MDKFactory.CreateProgram<Program>();
            Debug.Assert(program.AngleDiff(0.0f, 0.0f) == 0.0f);
            Debug.Assert(program.AngleDiff(0.0f, 360.0f) == 0.0f);
            Debug.Assert(program.AngleDiff(0.0f, 0.1f) == 0.1f);
            Debug.Assert(program.AngleDiff(0.1f, 0.0f) == -0.1f);
            Debug.Assert(program.AngleDiff(0.1f, 0.1f) == 0.0f);
            Debug.Assert(program.AngleDiff(0.1f, 0.5f) == 0.4f);
            Debug.Assert(program.AngleDiff(1.01f, 0.01f) == -1.0f);
            Debug.Assert(program.AngleDiff(0.0f, 180.0f) == 180.0f);
            Debug.Assert(Math.Abs(program.AngleDiff(180.0f, 0.0f)) == 180.0f);
            Debug.Assert(Math.Abs(program.AngleDiff(180.0f, 360.0f)) == 180.0f);
            Debug.Assert(Math.Abs(program.AngleDiff(360.0f, 180.0f)) == 180.0f);
            Debug.Assert(program.AngleDiff(360.0f, 0.1f) == 0.1f);
            Debug.Assert(program.AngleDiff(0.1f, 360.0f) == -0.1f);

            Debug.Assert(program.RadiansToDegrees(0.0f) == 0.0f);
            Debug.Assert(program.RadiansToDegrees((float)Math.PI) == 180.0f);
            Debug.Assert(program.RadiansToDegrees(2.0f * (float)Math.PI) == 360.0f);

            Debug.Assert(program.DegreesToRadians(0.0f) == 0.0f);
            Debug.Assert(program.DegreesToRadians(180.0f) == (float)Math.PI);
            Debug.Assert(program.DegreesToRadians(360.0f) == (float)Math.PI * 2.0f);
        }
    }
}