private IEnumerator<bool> running_;

private string startVentsName_, airlockInName_, airlockVentsName_, airlockOutName_, destVentsName_;

private IMyDoor airlockIn_ = null, airlockOut_ = null;

private List<IMyAirVent> startVents_ = new List<IMyAirVent>();
private List<IMyAirVent> airlockVents_ = new List<IMyAirVent>();
private List<IMyAirVent> destVents_ = new List<IMyAirVent>();

private bool lookupVents(string name, List<IMyAirVent> into)
{
    into.Clear();
    if (name == "Space")
        return true;

    IMyAirVent uniqueVent = (IMyAirVent)GridTerminalSystem.GetBlockWithName(name);
    if (uniqueVent != null)
    {
        into.Add(uniqueVent);
        return true;
    }
    var group = GridTerminalSystem.GetBlockGroupWithName(name);
    if (group == null)
        return false;
    List<IMyAirVent> tempGroup = new List<IMyAirVent>();
    group.GetBlocksOfType(tempGroup);
    foreach (var vent in tempGroup)
    {
        if (vent.Enabled && vent.CanPressurize)
            into.Add(vent);
    }
    return into.Count > 0;
}

private string checkConfig()
{
    if (!lookupVents(startVentsName_, startVents_))
        return startVentsName_;

    airlockIn_ = (IMyDoor)GridTerminalSystem.GetBlockWithName(airlockInName_);
    if (airlockIn_ == null)
        return airlockInName_;

    if (!lookupVents(airlockVentsName_, airlockVents_))
        return airlockVentsName_;

    airlockOut_ = (IMyDoor)GridTerminalSystem.GetBlockWithName(airlockOutName_);
    if (airlockOut_ == null)
        return airlockOutName_;

    if (destVentsName_ != "Space")
    {
        IMyBlockGroup ventGroup = null;

        ventGroup = GridTerminalSystem.GetBlockGroupWithName(destVentsName_);
        if (ventGroup == null)
            return destVentsName_;
        ventGroup.GetBlocksOfType(destVents_);
        if (destVents_.Count == 0)
            return destVentsName_;
    }

    return null;
}

private const string Help = "<vent group|Space>:<door user will enter>:<vent in airlock>:<door user will exit>:<vent group|Space>";

// Argument should be of the form:
// First Door:Vent:Second Door:Room Vent Group

// Returns error string or "".
private string parseArgument(string argument)
{
    var components = argument.Split(':');
    if (components.Count() != 5)
    {
        return "Expected argument to be: " + Help;
    }

    startVentsName_ = components[0];
    airlockInName_ = components[1];
    airlockVentsName_ = components[2];
    airlockOutName_ = components[3];
    destVentsName_ = components[4];

    if (startVentsName_ == airlockVentsName_)
        return "Start and Airlock vent names cannot be the same.";
    if (airlockVentsName_ == "Space")
        return "Airlock vents can't be space. That's insane, murderous wretch.";
    if (airlockVentsName_ == destVentsName_)
        return "Airlock and Destination vent names cannot be the same.";
    if (startVentsName_ == destVentsName_)
        return "Start and Destination vent names cannot be the same.";

    var problem = checkConfig();
    if (problem != null)
        return $"Invalid argument, could not look up '{problem}'";

    return null;
}

public void Main(string argument, UpdateType updateType) 
{
    if (updateType == UpdateType.Terminal)
    {
        if (argument == "")
        {
            Echo("ERROR: Specify which blocks form the airlock: " + Help);
            return;
        }
        if (running_ != null)
        {
            Echo("ERROR: Airlock sequence already in progress.");
            return;
        }

        // Argument should be: Prefix:[PI|PE|SI|SE]
        string error = parseArgument(argument);
        if (error != null)
        {
            Echo($"ERROR: {error}");
            return;
        }

        running_ = airlockCycle();
        Runtime.UpdateFrequency |= UpdateFrequency.Once;
        return;
    }

    execute();
}

private void Finish(string result)
{
    if (result != null && result != "")
    {
        Echo(result);
    }
    if (running_ != null)
    {
        running_.Dispose();
        running_ = null;
    }
}

private int numTicks = 0;

public void execute()
{
    ++numTicks;
    var problem = checkConfig();
    if (problem != null)
    {
        Finish($"ERROR: {problem}");
        numTicks = 0;
        return;
    }
    Echo($"#{numTicks}...");
    bool continuing = running_.MoveNext();
    if (!continuing)
    {
        Finish("Complete.");
        numTicks = 0;
        return;
    }

    Runtime.UpdateFrequency |= UpdateFrequency.Update10;
}

private bool isSpace(List<IMyAirVent> group)
{
    return group.Count == 0;
}

private float getPressure(List<IMyAirVent> group)
{
    return isSpace(group) ? 0.0f : group[0].GetOxygenLevel();
}

private bool isDepressurizing(List<IMyAirVent> group)
{
    foreach (var vent in group)
    {
        if (vent.Depressurize)
            return true;
    }
    return false;
}

private void depressurize(List<IMyAirVent> group, bool enabled)
{
    foreach (var vent in group)
    {
        vent.Enabled = true;
        vent.Depressurize = enabled;
    }
}

private bool equalized(List<IMyAirVent> room)
{
    var roomPressure = getPressure(room);
    if ((float)((int)roomPressure) == roomPressure)
    {
        return roomPressure == getPressure(airlockVents_);
    }
    return false;
}

private bool equalizeWith(List<IMyAirVent> room)
{
    if (equalized(room))
    {
        depressurize(airlockVents_, getPressure(room) == 0.0f);
        return true;
    }
    else
    {
        depressurize(airlockVents_, isDepressurizing(room));
        return false;
    }
}

private bool openDoor(IMyDoor door)
{
    if (door.OpenRatio >= 1.0f)
    {
        door.Enabled = false;
        return true;
    }
    door.Enabled = true;
    door.OpenDoor();
    return false;
}

private bool closeDoor(IMyDoor door)
{
    if (door.OpenRatio <= 0.0f)
    {
        door.Enabled = false;
        return true;
    }
    door.Enabled = true;
    door.CloseDoor();
    return false;
}

public void alert()
{
    ///TODO
}

public IEnumerator<bool> airlockCycle() 
{
    const int tickTime = 100;
    Echo("Check equalization");
    // Equalize the airlock with the outer vents.
    if (equalized(startVents_) && equalized(destVents_))
    {
        Echo("Opening doors");
        for (int ticks = 0; ticks < tickTime; ++ticks)
        {
            bool working = false;
            if (openDoor(airlockIn_))
                working = true;
            if (openDoor(airlockOut_))
                working = true;
            if (!working)
                break;
        }
        yield return false;
    }

    // If the airlock needs cycling, close the far door.
    for (int ticks = 0; ticks < tickTime; ++ticks)
    {
        if (equalized(startVents_) || closeDoor(airlockOut_))
            break;
        Echo("Closing far door");
        yield return true;
    }

    // Match the airlock with your room
    for (int ticks = 0; ticks < 30; ++ticks)
    {
        if (equalizeWith(startVents_))
            break;

        Echo("Equalizing");
        closeDoor(airlockOut_);

        yield return true;
    }

    // Grant access to the airlock
    for (int ticks = 0; !openDoor(airlockIn_) && ticks < tickTime; ++ticks)
    {
        Echo("Opening near door");
        yield return true;
    }

    // Wait N seconds
    for (int ticks = 0; ticks < tickTime; ++ticks)
    {
        Echo("Waiting");
        Echo($"{(tickTime - ticks) / 10}...");

        openDoor(airlockIn_);
        closeDoor(airlockOut_);

        yield return true;
    }

    // Close ingress door
    for (int ticks = 0; ticks < tickTime; ++ticks)
    {
        if (closeDoor(airlockIn_))
            break;

        yield return true;
    }

    // Cycle airlock to your destination
    for (int ticks = 0; ticks < 30; ++ticks)
    {
        if (equalizeWith(destVents_))
            break;

        closeDoor(airlockIn_);
        closeDoor(airlockOut_);

        yield return true;
    }

    for (int ticks = 0; ticks < tickTime; ++ticks)
    {
        // Open the exit door
        openDoor(airlockOut_);
        closeDoor(airlockIn_);

        yield return true;
    }
}