private List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
private List<IMyTerminalBlock> displays = new List<IMyTerminalBlock>();

private IMyPistonBase piston_;
private IMyMotorStator rotor_;
float lastPistonPosition_ = 0;
float lastRotorAngle_ = 0;
float pistonPosition_ = 0;
float rotorAngle_ = 0;
float rotorAngleDeg_ = 0;
int pistonPositionTicks_ = 0;
int rotorAngleTicks_ = 0;
long pistonId_ = -1;
long rotorId_ = -1;
float seekGoal_ = 0.0f;
int step_ = 0;

private Dictionary<string, string> config_ = new Dictionary<string, string>();
private string defaults_ = @"
drills = *Drills
rotor = *Rotor
piston = *Piston
start_height = 0.0
descent_step = 0.7
rotate_rpm = 0.8
";

enum State
{
    Begin,          // Haven't done anything yet,
    SeekingStart,
    StartDrills,
    StartRotation,
    Rotating,       // Drill is rotating,
    StopRotating,
    SeekDown,       // Drill is descending to next tier,
    Ending,
    Aborted,
};
private State state_;

public Program()
{
    state_ = State.Begin;

    ParseCustomData(defaults_);
    ParseCustomData(Me.CustomData);
}

void ParseCustomDataValue(string key, string value)
{
    config_[key] = value;
}

bool ExtractFields(string line, out string key, out string value)
{
    var comment = line.IndexOf("#");
    if (comment != -1)
        line = line.Substring(0, comment);
    line = line.Trim();
    if (line == String.Empty) {
        key = "";
        value = "";
        return false;
    }
    var equals = line.IndexOf("=");
    if (equals == -1) {
        key = line;
        value = "true";
    } else {
        key = line.Substring(0, equals).Trim();
        value = line.Substring(equals + 1).Trim();
    }
    return true;
}

void ParseCustomData(string data)
{
    foreach (var line in data.Split('\n'))
    {
        string key, value;
        if (ExtractFields(line, out key, out value)) {
            ParseCustomDataValue(key, value);
        }
    }
}

private void findElements()
{
    piston_ = (IMyPistonBase)GridTerminalSystem.GetBlockWithName(config_["piston"]);
    rotor_ = (IMyMotorStator)GridTerminalSystem.GetBlockWithName(config_["rotor"]);
}

private void updateVariables()
{
    lastPistonPosition_ = pistonPosition_;
    pistonPosition_ = piston_.CurrentPosition;
    if (piston_.EntityId != pistonId_)
    {
        report($"Selected piston");
        pistonId_ = piston_.EntityId;
        lastPistonPosition_ -= 0.001f;
    }
    if (lastPistonPosition_ != pistonPosition_)
        pistonPositionTicks_ = 0;
    pistonPositionTicks_++;

    lastRotorAngle_ = rotorAngle_;
    rotorAngle_ = rotor_.Angle;
    rotorAngleDeg_ = (float)(rotorAngle_ * 180.0f / Math.PI);
    if (rotor_.EntityId != rotorId_)
    {
        report($"Selected rotor");
        rotorId_ = rotor_.EntityId;
        lastRotorAngle_ -= 0.001f;
    }
    if (lastRotorAngle_ != rotorAngle_)
        rotorAngleTicks_ = 0;
    rotorAngleTicks_++;
}

private void report(string text)
{
    Echo(text);
}

private void setGroupEnabled(string groupName, bool enabled)
{
    string state = (enabled ? "en" : "dis") + "able";
    var group = GridTerminalSystem.GetBlockGroupWithName(groupName);
    if (group != null) {
        group.GetBlocks(blocks);
        foreach (IMyFunctionalBlock block in blocks) {
            block.Enabled = enabled;
        }
        report($"{state}d {blocks.Count()} {groupName}(s).");
    }
}

private State abort(string reason)
{
    report($"ABORT: {reason}");
    setGroupEnabled(config_["drills"], false);
    if (piston_ != null) piston_.Enabled = false;
    if (rotor_ != null) {
        rotor_.Enabled = false;
        rotor_.RotorLock = true;
    }
    Runtime.UpdateFrequency = UpdateFrequency.Once;
    return State.Aborted;
}

private void reportConfig()
{
    var drills = config_["drills"];
    var rotor = config_["rotor"];
    var piston = config_["piston"];
    var descent_step = config_["descent_step"];
    var rotate_rpm = config_["rotate_rpm"];

    Echo($"drills={drills}, rotor={rotor}, piston={piston}, descent_step={descent_step}, rotate_rpm={rotate_rpm}");
}

private State tick()
{
    findElements();
    if (piston_ == null || rotor_ == null)
        return abort("Missing *Piston/*Rotor");
    updateVariables();
    if (state_ != State.Begin)
        report($"Step {step_}");

    switch (state_)
    {
        case State.Begin:
            report("Begin");
            reportConfig();
            setGroupEnabled(config_["drills"], false);
            rotor_.Enabled = false;
            piston_.Enabled = false;
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            return State.SeekingStart;

        case State.SeekingStart:
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            reportConfig();
            double start_height = (double)((int)float.Parse(config_["start_height"]) * 1000) / 1000.0;
            var approx_position = (double)((int)(pistonPosition_ * 1000)) / 1000.0;
            if (approx_position != start_height) {
                var sign = (approx_position < start_height) ? -1.0 : 1.0;
                var delta = Math.Abs(approx_position - start_height);
                var velocity = Math.Max(delta / 2.0, 0.1);
                piston_.Velocity = sign * (float)Math.Min(velocity, 2.0);
                piston_.Enabled = true;
                report($"Piston: {approx_position} -> {start_height} @ {piston_.Velocity}");
            } else {
                report("Piston homed");
                piston_.Enabled = false;;
            }

            approx_position = (double)((int)(rotorAngleDeg_ * 100.0)) / 100.0;
            if (approx_position > 0.1) {
                double movement = 0.0;
                double direction = 0.0;
                if (approx_position > 180.0) {
                    movement = 360.0 - approx_position;
                    direction = 1.0;
                } else {
                    movement = approx_position;
                    direction = -1.0;
                }
                var velocity = Math.Max(approx_position / 15, 0.001);
                rotor_.TargetVelocityRPM = (float)(direction * Math.Min(velocity, 2.0));
                rotor_.Enabled = true;
                rotor_.RotorLock = false;
                report($"Rotor: {approx_position}, seek {rotor_.TargetVelocityRPM}, {velocity}");
            } else {
                report("Rotor homed");
                rotor_.Enabled = false;
                rotor_.RotorLock = true;
            }

            if (!piston_.Enabled && !rotor_.Enabled)
                return State.StartDrills;
            return State.SeekingStart;
        }

        case State.StartDrills:
            report("Start drills");
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            setGroupEnabled("*Drills", true);
            step_ = 1;
            return State.StartRotation;

        case State.StartRotation:
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            piston_.Enabled = false;
            rotor_.RotorLock = false;
            rotor_.Enabled = true;
            rotor_.TargetVelocityRPM = 0.1f;
            report($"Start rotation @ {rotorAngleDeg_}");
            return (rotorAngleDeg_ < 330.0) ? State.Rotating : State.StartRotation;
        }

        case State.Rotating:
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            if (rotorAngleDeg_ > 345.0)
            {
                Runtime.UpdateFrequency = UpdateFrequency.Update10;
                rotor_.TargetVelocityRPM *= 0.75f;
                return State.StopRotating;
            }
            report($"Piston: {pistonPosition_}");
            float rotor_rpm = float.Parse(config_["rotate_rpm"]);
            rotor_.TargetVelocityRPM = (float)Math.Min(rotor_.TargetVelocityRPM + 0.1f, rotor_rpm);
            report($"Rotor: {rotorAngleDeg_}, {rotor_.TargetVelocityRPM}");
            return State.Rotating;
        }

        case State.StopRotating:
        {
            piston_.Velocity = 0.0f;
            piston_.Enabled = false;
    
            report($"Piston: {pistonPosition_}");
            if (rotorAngleDeg_ < 1.0f || rotorAngleDeg_ > 359.5f)
            {
                rotor_.RotorLock = true;
                rotor_.Enabled = false;
                float descentRate = float.Parse(config_["descent_step"]);
                seekGoal_ = pistonPosition_ + descentRate;
                if (seekGoal_ > piston_.MaxLimit + descentRate / 2)
                {
                    return abort($"Seek Goal would be {seekGoal_} > {piston_.MaxLimit}");
                }
                seekGoal_ = Math.Min(seekGoal_, piston_.MaxLimit);
                return State.SeekDown;
            }

            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            rotor_.TargetVelocityRPM = 0;
            rotor_.TargetVelocityRad = (float)(Math.PI * 2 - rotorAngle_);
            report($"Stopping: {rotorAngle_}, {rotor_.TargetVelocityRad}");
            return State.StopRotating;
        }

        case State.SeekDown:
        {
            if (pistonPosition_ >= seekGoal_)
            {
                report("Piston reached goal");
                piston_.Enabled = false;
                ++step_;
                return State.StartRotation;
            }

            if (pistonPositionTicks_ > 30)
                return abort("Piston stuck");

            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            piston_.Velocity = (float)Math.Min(Math.Max(0.1f, seekGoal_ - pistonPosition_) / 2, 1.0f);
            piston_.Enabled = true;
            report($"Piston seeking {pistonPosition_} -> {seekGoal_} @ {piston_.Velocity}");

            return State.SeekDown;
        }

        default:
            return abort("All she wrote");
    }
}

public void Main(string argument, UpdateType updateSource)
{
    var nextState = tick();
    // State change clobbers tick counters.
    if (nextState != state_) {
        pistonPositionTicks_ = 0;
        rotorAngleTicks_ = 0;
    }
    state_ = nextState;
}
