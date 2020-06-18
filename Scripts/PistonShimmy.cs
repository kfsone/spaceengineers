// So we don't have to keep reallocating the list.
private List<IMyPistonBase> pistons = new List<IMyPistonBase>();
private List<IMyTerminalBlock> displays = new List<IMyTerminalBlock>();

// So we can leave pistons we've driven alone.
private Dictionary<long, bool> completed = new Dictionary<long, bool>();

// For tracking whether a piston is stuck.
private const int StuckTicks = 5;
private int lastPosition = -1;
private int positionCount = 0;

public const int HeadedUp = -1;
public const int HeadedDown = 1;
public const int Undecided = 0;
public const int StartDelayTicks = 5;
public const int StartIndicatorTicks = 3;
public const int ShimmyTicks = 5;
public const float Speed = 0.3f;
public const string PrefixLabel = "Mining";
public const string PistonPrefix = PrefixLabel + " Piston S";
public const string AlarmLabel = PrefixLabel + " Indicator Light";

// Class members.
long   currentPiston = 0;
int    currentDirection = Undecided;
int    runningTicks = 0;
int    indicatorTicks = 0;
bool   enableShimmy = true;
bool   shimmyReversing = false;
int    nextShimmyTick = 0;
string currentReport = "";

public Program()
{
    pistons.Clear();
    completed.Clear();
    stopIndicator();
}

private void report(string text)
{
    Echo(text);
    currentReport += text + "\n";
}

private void halt()
{
    Runtime.UpdateFrequency = UpdateFrequency.Once;
    report("Halted.");
}

// Our goal is to get pistons to max. If they're anywhere but max, roll them up and then down.
private int decideDirection(IMyPistonBase piston)
{
    // If the piston is closer to the top than the bottom, we'll assume it's headed up.
    return piston.CurrentPosition > piston.MinLimit ? HeadedUp : HeadedDown;
}

private string direction()
{
    if (shimmyReversing)
        return "shimmy-up";
    switch (currentDirection)
    {
        case HeadedDown: return "down";
        case HeadedUp:   return "up";
        default:         return "undecided";
    }
}

private void chooseDirection(IMyPistonBase piston)
{
    if (runningTicks <= 0)
        report($"{-runningTicks}...");

    if (runningTicks == 0)
    {
        currentDirection = decideDirection(piston);

        startIndicator(StartIndicatorTicks);
        shimmyReversing = false;
        nextShimmyTick = runningTicks + ShimmyTicks;
    }
}

private void startIndicator(int forTicks)
{
    stopIndicator();
    var light = (IMyReflectorLight)GridTerminalSystem.GetBlockWithName(AlarmLabel);
    if (light != null)
    {
        light.Radius = 30.0f;
        light.Intensity = 10.0f;
        light.SetValue("Color", new Color(255, 0, 0));
        light.ApplyAction("OnOff_On");
        indicatorTicks = forTicks;
    }
    else
        indicatorTicks = 0;
}

private void stopIndicator()
{
    var light = (IMyReflectorLight)GridTerminalSystem.GetBlockWithName(AlarmLabel);
    if (light != null)
        light.ApplyAction("OnOff_Off");
    indicatorTicks = 0;
}

public void Reset()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update100;

    currentPiston = 0;
    currentDirection = Undecided;
    runningTicks = 0;
    enableShimmy = true;
    nextShimmyTick = 0;
    shimmyReversing = false;

    lastPosition = -1;
    positionCount = 0;

    stopIndicator();
}

private bool isCandidate(IMyPistonBase piston, bool allowMaximized)
{
    if (piston.Enabled == false)
        return false;

    if (!piston.DisplayNameText.StartsWith(PistonPrefix))
        return false;

    if (piston.CurrentPosition >= piston.MaxLimit)
    {
        if (allowMaximized)
            return true;
        bool hasRun = false;
        completed.TryGetValue(piston.EntityId, out hasRun);
        return hasRun == false;
    }

    return true;
}

private IMyPistonBase findPiston()
{
    if (currentPiston != 0)
    {
        var piston = (IMyPistonBase)GridTerminalSystem.GetBlockWithId(currentPiston);
        if (piston != null)
        {
            return piston;
        }
    
        // Extant piston has gone away.
        Reset();
        return null;
    }

    // Find a piston to work on.
    GridTerminalSystem.GetBlocksOfType<IMyPistonBase>(pistons);
    foreach (var piston in pistons)
    {
        if (!isCandidate(piston, false))
            continue;

        Reset();

        completed[piston.EntityId] = false;
        currentPiston = piston.EntityId;
        currentDirection = Undecided;
        runningTicks = -StartDelayTicks;

        return piston;
    }

    Reset();
    return null;
}

private void shimmy(IMyPistonBase piston)
{
    // Do nothing when the piston is being retracted.
    if (currentDirection == HeadedUp)
        return;

    if (!enableShimmy || runningTicks < nextShimmyTick)
        return;

    // Switch the shimmy direction.
    shimmyReversing = !shimmyReversing;

    // Only reverse for half as long as we go forward.
    nextShimmyTick = runningTicks + ShimmyTicks / (shimmyReversing ? 2 : 1);
}

private void setSpeed(IMyPistonBase piston)
{
    // Reverse at half speed
    var speed = Speed * currentDirection;
    if (shimmyReversing)
    {
        speed = -(speed / 2);
    }
    piston.Velocity = speed;
}

private bool stuck(IMyPistonBase piston)
{
    int position = (int)(piston.CurrentPosition * 100.0f);
    if (position != lastPosition)
        positionCount = 0;
    else
        positionCount++;
    if (positionCount > 0)
        report($"At {piston.CurrentPosition} for {positionCount} ticks");
    lastPosition = position;

    if (positionCount < StuckTicks)
        return false;

    lastPosition = position;

    if (currentDirection == HeadedUp)
    {
        report("Piston Stuck: Reversing.");
        reverse();
        return true;
    }

    if (shimmyReversing == true)
    {
        report("Piston got stuck, disabling shimmy.");
        enableShimmy = false;
        shimmyReversing = false;
        return true;
    }

    report("PISTON STUCK. Aborting.");
    Runtime.UpdateFrequency = UpdateFrequency.Once;
    piston.ApplyAction("OnOff_Off");
    stopIndicator();
    return true;
}

private void doTick()
{
    if (indicatorTicks > 0)
    {
        indicatorTicks--;
        if (indicatorTicks == 0)
            stopIndicator();
    }

    var piston = findPiston();
    if (piston == null)
    {
        report("Inactive");
        return;
    }

    report(piston.DisplayNameText);

    if (currentDirection == Undecided)
        chooseDirection(piston);
    if (runningTicks <= 0)
    {
        report("Alerting");
        return;
    }
    if (runningTicks == 0)
        setDrillsEnabled(true);

    ++runningTicks;

    report($"Range {piston.MinLimit}-{piston.MaxLimit}m");

    if (stuck(piston))
        return;

    // Consider a brief partial reverse of the piston.
    shimmy(piston);

    setSpeed(piston);

    report($"Going {direction()}@{piston.Velocity}m/s");
    report($"Position {piston.CurrentPosition}");
 
    if (currentDirection == HeadedUp && runningTicks > 0 && piston.CurrentPosition <= piston.MinLimit)
    {
        reverse();
        return;
    }

    if (piston.CurrentPosition >= piston.MaxLimit)
    {
        completed[piston.EntityId] = true;
        Reset();
        return;
    }
}

private void reverse()
{
    // Reverse direction by resetting but immediately reselecting the same piston.
    var pistonId = currentPiston;
    Reset();
    currentPiston = pistonId;
    currentDirection = HeadedDown;
    startIndicator(StartIndicatorTicks);
}

private void showReport(IMyTextSurfaceProvider block)
{
    block.GetSurface(0).WriteText(currentReport);
}

private void setDrillsEnabled(bool enabled)
{
    string state = (enabled ? "en" : "dis") + "able";
    var drillGroup = GridTerminalSystem.GetBlockGroupWithName("Mining Drills");
    var drills = new List<IMyShipDrill>();
    drillGroup?.GetBlocksOfType(drills, drill => drill.Enabled = enabled);
    report($"{state}d {drills.Count()} drill(s).");
}

private void setAllPistons(bool enabled, float velocity)
{
    GridTerminalSystem.GetBlocksOfType<IMyPistonBase>(pistons);
    foreach (var piston in pistons)
    {
        piston.Enabled = enabled;
        piston.Velocity = velocity;
    }
    if (!enabled)
        report($"disabled {pistons.Count()} piston(s)");
    else
        report($"set {pistons.Count()} to {velocity}m/s");
}

private void doAbort()
{
    report("USER ABORT");
    // Turn off the drills.
    setDrillsEnabled(false);
    // Set all the pistons to retract.
    setAllPistons(false, -0.5f);
    halt();
}

public void Main(string argument, UpdateType updateSource)
{
    currentReport = "";
    if (argument == "abort")
    {
        doAbort();
    }
    else if (argument == "retract")
    {
        setDrillsEnabled(true);
        setAllPistons(true, -0.6f);
    }
    else if (argument == "extend")
    {
        setDrillsEnabled(true);
        setAllPistons(true, 0.1f);
    }
    else if (argument == "start")
    {
        Runtime.UpdateFrequency = UpdateFrequency.Update100;
    }
    else if (updateSource == UpdateType.Update100)
    {
        doTick();
    }

    if (currentReport != "")
    {
        showReport(Me);

        displays.Clear();
        GridTerminalSystem.SearchBlocksOfName("Mining Control", displays);
        foreach (var display in displays)
        {
            showReport((IMyTextSurfaceProvider)display);
        }
    }
}
 