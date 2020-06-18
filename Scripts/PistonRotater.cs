private List<IMyTerminalBlock> displays = new List<IMyTerminalBlock>();

public const string PrefixLabel = "Mining";
public const string PistonPrefix = PrefixLabel + " Piston S";
public const string AlarmLabel = PrefixLabel + " Indicator Light";
public const string MiningDisplays = "Mining Control";
public const string MiningRotor = "Mining Top Rotor";
public const string MiningPiston = "Mining Piston 1";
public const string DrillsGroup = "Mining Drills";

private string currentReport = "";
private int runningTicks;

public Program()
{
}

private void report(string text)
{
    currentReport += text + "\n";
}

private void abortWith(string reason)
{
    report($"ABORT: {reason}");
    Runtime.UpdateFrequency = UpdateFrequency.Once;
}

private void showReport(IMyTextSurfaceProvider block)
{
    block.GetSurface(0).WriteText(currentReport);
}

private void setDrillsEnabled(bool enabled)
{
    string state = (enabled ? "en" : "dis") + "able";
    var drillGroup = GridTerminalSystem.GetBlockGroupWithName(DrillsGroup);
    var drills = new List<IMyShipDrill>();
    drillGroup?.GetBlocksOfType(drills, drill => drill.Enabled = enabled);
    report($"{state}d {drills.Count()} drill(s).");
}

private IMyMotorStator getRotor()
{
    IMyMotorStator rotor = (IMyMotorStator)GridTerminalSystem.GetBlockWithName(MiningRotor);
    if (rotor?.Enabled == true)
        return rotor;
    return null;
}

private void doRotate()
{
    var rotor = getRotor();
    if (rotor == null)
    {
        abortWith("No rotor.");
        return;
    }

    runningTicks++;

    IMyPistonBase piston = (IMyPistonBase)GridTerminalSystem.GetBlockWithName(MiningPiston);
    if (piston != null)
    {
        var tickAngle = (double)((runningTicks * 5) % 400);
        if (tickAngle > 40)
        {
            tickAngle -= 40;
            piston.Velocity = (float)Math.Sin(Math.PI * tickAngle / 180.0) / 2;
            piston.Enabled = true;
        }
        else
        {
            piston.Enabled = false;
        }
    }

    var subTick = runningTicks % 60;
    if (subTick < 40)
    {
        rotor.TargetVelocityRPM = 0.8f;
        rotor.RotorLock = false;
    }
    else if (subTick < 44)
    {
        rotor.RotorLock = true;
    }
    else if (subTick < 56)
    {
        rotor.TargetVelocityRPM = -0.5f;
        rotor.RotorLock = false;
    }
    else
    {
        rotor.RotorLock = true;
    }

    report($"Rotor @ {rotor.Angle}/{rotor.TargetVelocityRPM}");
    if (piston != null && piston.Enabled)
        report($"Piston @ {piston.CurrentPosition}/{piston.Velocity}");
}

public void startRotate()
{
    if (getRotor() == null)
    {
        abortWith($"No rotor: {MiningRotor}.");
        return;
    }

    report("Starting!");

    setDrillsEnabled(true);
    Runtime.UpdateFrequency = UpdateFrequency.Update100;
    runningTicks = 0;
}

public void Main(string argument, UpdateType updateSource)
{
    currentReport = "";
    
    if (updateSource == UpdateType.Terminal)
    {
        startRotate();
    }
    else if (updateSource == UpdateType.Update100)
    {
        report($"Tick {runningTick}");
        doRotate();
    }

    if (currentReport != "")
    {
        displays.Clear();
        GridTerminalSystem.SearchBlocksOfName(MiningDisplays, displays);
        foreach (var display in displays)
        {
            if (display.EntityId != Me.EntityId)
                showReport((IMyTextSurfaceProvider)display);
        }
        showReport(Me);       

        Echo(currentReport);
    }
}
 