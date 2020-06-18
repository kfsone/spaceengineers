private List<IPiston> pistons_ = new List<IPiston>();
private List<IRotor> rotors_ = new List<IRotor>();
private List<IPiston> arms_ = new List<IPiston>();
private List<IMyShipDrill> drills_ = new List<IMyShipDrill>();

private List<IMyTerminalBlock> blocks_ = new List<IMyTerminalBlock>();

private Dictionary<string, string> config_ = new Dictionary<string, string>();

private IEnumerator<bool> action_ = null;

interface IAdjustable
{
    float Value { get; }
    float Maximum { get; }
    float Minimum { get; }

    float Rate { get; set; }

    void SetEnabled(bool enable);
};

class IPiston : IMyPistonBase, IAdjustable
{
    float Value { get => CurrentPosition; }
    float Maximum { get => MaxLimit; }
    float Minimum { get => MinLimit; }
    float Rate { get => Velocity; set => Velocity = value; }

    void SetEnabled(bool enable) => Enabled = enable;
};

class IRotor : IMyMotorStator, IAdjustable
{
    float Value { get => Angle * 180.0f / Math.PI; }
    float Maximum { get => 360.0f; }
    float Minimum { get => 180.0f; }
    float Rate { get => TargetVelocityRad * 180.0f / Math.PI; set => TargetVelocityRad = value * Math.PI / 180.0f; }

    void SetEnabled(bool enable)
    {
        Enabled = enable;
        RotorLock = !enable;
    }
};

public Program()
{
    config_["piston"] = "*Piston";
    config_["rotor"] = "*Rotor";
    config_["arms"] = "*Arms";
    config_["drills"] = "*Drills";
}

private void LookupBlockName<IMyT, IT=IMyT>(string name, List<IT> into)
    where T : class, IMyTerminalBlock
    where IT : class, IMyFunctionalBlock
{
    into.Clear();

    // Is it a group?
    var group = GridTerminalSystem.GetBlockGroupWithName(name);
    if (group != null) {
        group.GetBlocksOfType(into);
        return;
    }

    // Search based on name.
    blocks_.Clear();
    GridTerminalSystem.SearchBlocksOfName(name, blocks_, block => block is IMyT );
    foreach (var block in blocks_) {
        IT tblock = (IT)block;
        if (tblock != null)
            into.Add(tblock);
    }
}

private void setBlocksEnabled<T>(List<T> blocks, bool enabled)
    where T : class, IMyFunctionalBlock
{
    foreach (IMyFunctionalBlock block in blocks_) {
        if (block != null)
            block.Enabled = enabled;
    }
}

private void Refresh()
{
    LookupBlockName<IMyPistonBase, IPiston>(config_["piston"], pistons_);
    LookupBlockName<IMyMotorStator, IRotor>(config_["rotor"], rotor_);
    LookupBlockName<IMyPistonBase, IPiston>(config_["arms"], arms_);
    LookupBlockName<IMyShipDrill>(config_["drills"], drills_);
}

public void usage()
{
    Echo("Usage: <piston | arms | turn> [option]");
}

private void Seek(IAdjustable item, float target, float maxSpeed)
{
    var approxPosition = (float)Math.Truncate(100.0f * item.Value) / 100.0f;
    if (approxPosition == target) {
        Echo($"{i}: Ready [{approxPosition}]");
        item.Rate = 0.0f;
        item.SetEnabled(false);
        return true;
    }

    var speed = 0.0f;
    var delta = target - item.Value;
    if (delta >= 0.0f) {
        speed = Math.Max(Math.Min(delta, maxSpeed), 0.01f);
    } else {
        speed = Math.Min(Math.Max(delta, -maxSpeed), -0.01f);
    }

    item.Rate = speed;
    item.SetEnabled(true);

    Echo($"{i}: {approxPosition}->{target} @ {Math.Truncate(1000 * item.Rate) / 1000}");

    return false;
}

private IEnumerator<bool> pistonAdjustment(float distance)
{
    List<float> targets = new List<float>();
    foreach (var piston in pistons_)
    {
        float pistonTarget = piston.CurrentPosition + distance;
        pistonTarget = Math.Min(pistonTarget, piston.MaxLimit);
        pistonTarget = Math.Max(pistonTarget, piston.MinLimit);
        targets.Add((float)Math.Truncate(100.0f * pistonTarget) / 100.0f);
        piston.Velocity = 0.0f;
        piston.Enabled = false;
    }

    Echo($"Adjusting {targets.Count()} pistons");

    char[] spinner = new char[4]{'|', '/', '-', '\\' };
    for (int tick = 0; tick < 10000; ++tick) {
        Echo($"{spinner[tick % spinner.Length]}");
        var ready = 0;
        var maxSpeed = Math.Min(((float)tick) * 0.01f, 0.5f);
        for (var i = 0; i < targets.Count(); ++i) {
            if (Seek(pistons_[i], targets[i], maxSpeed))
                ++ready;
        }

        if (ready >= targets.Count())
            break;

        yield return true;
    }
    yield return false;
}

private void adjustPiston(string argument)
{
    float distance = float.Parse(argument);
    Echo($"distance = {distance}");
    action_ = pistonAdjustment(distance);
    Echo($"action_ = {action_}");
}

private void adjustArms(string argument)
{
}

private void adjustRotor(string argument)
{
}

private void halt()
{
    Echo("Stopping rotor");
    setBlocksEnabled(rotor_, false);
    Echo("Stopping arms");
    setBlocksEnabled(arms_, false);
    Echo("Stopping piston");
    setBlocksEnabled(pistons_, false);
    Echo("Stopping drills");
    setBlocksEnabled(drills_, false);
}

private bool handleInput(string argument)
{
    var arguments = argument.Trim().Split(' ');
    if (arguments.Count() < 0 || string.IsNullOrEmpty(arguments[0])) {
        usage();
        return false;
    }

    switch (arguments[0]) {
        case "piston": {
            if (arguments.Count() == 2)
            {
                adjustPiston(arguments[1]);
                return true;
            }
            break;
        }
        case "arms": {
            if (arguments.Count() == 2)
            {
                adjustArms(arguments[1]);
                return true;
            }
            break;
        }
        case "turn": {
            if (arguments.Count() == 2)
            {
                adjustRotor(arguments[1]);
                return true;
            }
            break;
        }
        case "stop": {
            halt();
            return true;
        }
        default: {
            Echo($"Unknown command: {arguments[0]}");
            break;
        }
    }

    usage();
    return false;
}

private int Count<IT>(List<IT> enumerable, Func<IT, bool> predicate)
    where IT : class, IMyEntity
{
    int value = 0;
    enumerable.ForEach(item => value += predicate(item) ? 1 : 0);
    return value;
}

public void Main(string argument, UpdateType updateSource)
{
    var activePistons = Count(pistons_, piston => piston.Enabled);

    if (updateSource == UpdateType.Terminal)
    {
        if (!handleInput(argument))
            return;

        Refresh();
    }

    Echo($"P:{pistons_.Count} R:{rotor_.Count} D:{drills_.Count} A:{arms_.Count}");

    if (action_ != null)
    {
        bool continuing = action_.MoveNext();
        if (continuing)
        {
            Runtime.UpdateFrequency |= UpdateFrequency.Once;
            return;
        }

        Echo("-End-");
        action_.Dispose();
        action_ = null;
    }
}

private bool workToDo()
{
    return true;
}

private IEnumerator<bool> armChurn()
{
    while (workToDo()) {
        float marker = rotors_[0]
    }
}