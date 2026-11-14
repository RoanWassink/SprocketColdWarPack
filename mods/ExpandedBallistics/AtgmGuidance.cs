using System.Numerics;

namespace SprocketExpandedBallistics;

internal sealed record AtgmSettings(double FlightSpeed = 200, double MaxTurnRate = 20,
    double MaximumFlightTime = 25, double GuidanceDelay = .25, string GuidanceMode = "sight",
    double? LaunchSpeed = null, string LaunchSpeedMode = "fixed", double LaunchSpeedMultiplier = 1,
    double Acceleration = 0, double MotorDelay = 0, double MotorBurnTime = 0, double CoastDeceleration = 0,
    double TopAttackAltitude = 60, double TopAttackClimbAngle = 25, double TopAttackDiveAngle = 55,
    AtgmFlightProfile? FlightProfile = null);

// Gameplay guidance only: bounded rotation, constant powered speed. Top attack steers to a
// prepared player lock point (cruise above it, then a steep dive); no seeker or terminal homing.
internal static class AtgmGuidance
{
    // Native Release may be inlined. Input ownership validates the pool directly.
    internal static bool LiveFlight(bool mapped, int expectedId, int actualId, double expectedSpawn,
        double actualSpawn, string expectedDefinition, string actualDefinition, int flags) =>
        mapped && expectedId == actualId && expectedSpawn == actualSpawn &&
        expectedDefinition == actualDefinition && (flags & 1) != 0 && (flags & 14) == 0;
    internal static bool KeyboardWindow(double now,double spawn,double maximum) =>
        double.IsFinite(now) && double.IsFinite(spawn) && double.IsFinite(maximum) &&
        now >= spawn && now-spawn < maximum;
    internal static double InitialSpeed(AtgmSettings s, double cannonVelocity)
    {
        if (!double.IsFinite(cannonVelocity) || cannonVelocity < 0) throw new ArgumentOutOfRangeException(nameof(cannonVelocity));
        return s.LaunchSpeedMode == "cannon"
            ? Math.Clamp(cannonVelocity*s.LaunchSpeedMultiplier,10,s.FlightSpeed)
            : s.LaunchSpeed ?? s.FlightSpeed;
    }
    internal static double SpeedAtAge(AtgmSettings s, double initialSpeed, double age)
    {
        if (!double.IsFinite(initialSpeed) || !double.IsFinite(age) || initialSpeed < 0 || age < 0)
            throw new ArgumentOutOfRangeException(nameof(age));
        var poweredAge=Math.Max(0,age-s.MotorDelay);
        var burnAge=s.MotorBurnTime>0 ? Math.Min(poweredAge,s.MotorBurnTime) : poweredAge;
        var peak=Math.Min(s.FlightSpeed,initialSpeed+s.Acceleration*burnAge);
        var coastAge=s.MotorBurnTime>0 ? Math.Max(0,poweredAge-s.MotorBurnTime) : 0;
        return Math.Max(10,peak-s.CoastDeceleration*coastAge);
    }
    internal static Vector3 AiIntercept(Vector3 target, Vector3 estimatedVelocity, Vector3 launch, AtgmSettings s)
    {
        // Game AI prediction only: retain its estimated target/velocity, omit shell drop.
        var point=target;
        for(var i=0;i<3;i++)point=target+estimatedVelocity*(Vector3.Distance(point,launch)/(float)s.FlightSpeed);
        return point;
    }
    internal static bool FreshAim(double now,double sampleTime) => double.IsFinite(now) && double.IsFinite(sampleTime) && now>=sampleTime && now-sampleTime<.3;
    internal static Vector3 AimAlongRay(Vector3 origin, Vector3 direction, Vector3 missilePosition, double flightSpeed)
    {
        var axis=Vector3.Normalize(direction);
        var along=Vector3.Dot(missilePosition-origin,axis);
        // Aim one second ahead on the sight ray, rather than chase a fixed convergence point.
        return origin+axis*Math.Max((float)flightSpeed,along+(float)flightSpeed);
    }
    internal static void Validate(ShellProfile p)
    {
        var s = p.Atgm;
        if (s is null) return;
        void Range(string field, double value, double min, double max)
        {
            if (!double.IsFinite(value) || value < min || value > max)
                throw new FormatException(ShellBallistics.RangeError(p.Id, field, value, min, max));
        }
        Range("flightSpeed", s.FlightSpeed, 50, 1000);
        if (s.LaunchSpeed is {} launch) Range("launchSpeed", launch, 10, s.FlightSpeed);
        Range("launchSpeedMultiplier", s.LaunchSpeedMultiplier, .01, 4);
        Range("acceleration", s.Acceleration, 0, 2000);
        Range("motorBurnTime", s.MotorBurnTime, 0, 60);
        Range("coastDeceleration", s.CoastDeceleration, 0, 100);
        Range("motorDelay", s.MotorDelay, 0, 5);
        if (p.Flight==null && s.Acceleration > 0 && s.MotorDelay >= s.MaximumFlightTime)
            throw new FormatException($"Profile '{p.Id}': motorDelay must be less than maximumFlightTime when acceleration is enabled.");
        if (s.LaunchSpeedMode is not ("fixed" or "cannon"))
            throw new FormatException($"Profile '{p.Id}': launchSpeedMode is '{s.LaunchSpeedMode}'; expected fixed or cannon.");
        Range("maxTurnRate", s.MaxTurnRate, 0, 90);
        Range("maximumFlightTime", s.MaximumFlightTime, 1, 60);
        Range("guidanceDelay", s.GuidanceDelay, 0, 5);
        if (p.Flight==null && s.GuidanceDelay >= s.MaximumFlightTime)
            throw new FormatException(FormattableString.Invariant($"Profile '{p.Id}': guidanceDelay is {s.GuidanceDelay}; expected less than maximumFlightTime ({s.MaximumFlightTime})."));
        if (s.GuidanceMode is not ("sight" or "keyboard" or "topattack" or "none"))
            throw new FormatException($"Profile '{p.Id}': guidanceMode is '{s.GuidanceMode}'; expected sight, keyboard, topattack or none.");
        Range("topAttackAltitude", s.TopAttackAltitude, 5, 300);
        Range("topAttackClimbAngle", s.TopAttackClimbAngle, 5, 85);
        Range("topAttackDiveAngle", s.TopAttackDiveAngle, 10, 85);
        if (s.FlightProfile is { } tuning)
        {
            tuning.Validate(p.Id);
            if (tuning.ShortRangeGuidanceDelay >= s.MaximumFlightTime)
                throw new FormatException($"Profile '{p.Id}': shortRangeGuidanceDelay must be less than maximumFlightTime.");
            // The hump is gun-launched SACLOS only; it is not a lock or top attack.
            if (tuning.RangePattern == "hump" && (p.Behavior != "atgm_gun" || s.GuidanceMode != "sight"))
                throw new FormatException($"Profile '{p.Id}': rangePattern hump requires atgm_gun behavior with sight guidance.");
        }
    }

    internal static Vector3 KeyboardStep(Vector3 heading, double yaw, double pitch, AtgmSettings settings, double dt)
    {
        var current = Vector3.Normalize(Step(heading, null, settings, dt));
        if (!double.IsFinite(yaw) || !double.IsFinite(pitch)) throw new ArgumentOutOfRangeException(nameof(yaw));
        if (settings.GuidanceMode != "keyboard") return current * (float)settings.FlightSpeed;
        yaw = Math.Clamp(yaw, -1, 1); pitch = Math.Clamp(pitch, -1, 1);
        var right = Vector3.Cross(Vector3.UnitY, current);
        // At a vertical heading keep a finite pitch axis instead of dividing by zero.
        right = right.LengthSquared() < 1e-8f ? Vector3.UnitX : Vector3.Normalize(right);
        var axis = Vector3.UnitY * (float)yaw - right * (float)pitch;
        var strength = Math.Min(1, Math.Sqrt(yaw*yaw + pitch*pitch));
        if (axis.LengthSquared() > 1e-8f && strength > 0)
            current = Vector3.Normalize(Vector3.Transform(current, Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis),
                (float)(strength * settings.MaxTurnRate * Math.PI / 180 * dt))));
        return current * (float)settings.FlightSpeed;
    }

    internal static Vector3 Step(Vector3 heading, Vector3? desiredDirection, AtgmSettings settings, double deltaSeconds)
    {
        static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
        if (!Finite(heading) || heading.LengthSquared() < 1e-8f || !double.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(heading));
        var current = Vector3.Normalize(heading);
        if (desiredDirection is { } desired && Finite(desired) && desired.LengthSquared() > 1e-8f && settings.GuidanceMode is "sight" or "topattack")
        {
            var target = Vector3.Normalize(desired);
            var dot = Math.Clamp(Vector3.Dot(current, target), -1, 1);
            // Do not chase an aim point behind the rocket or make a U-turn after passing it.
            // A loft pitch-over can legitimately exceed90deg (climb45 -> dive70).
            // Keep SACLOS's rear-target guard; topattack permits that vertical arc
            // only while its horizontal command stays forward, never a target U-turn.
            var forwardPitchOver=settings.GuidanceMode=="topattack"&&current.X*target.X+current.Z*target.Z>=0;
            if (dot > 0 || forwardPitchOver && dot>-.999999f)
            {
                var angle = Math.Acos(dot);
                var allowed = settings.MaxTurnRate * Math.PI / 180 * deltaSeconds;
                if (angle <= allowed) current = target;
                else if (allowed > 0 && angle > 1e-6)
                {
                    var axis = Vector3.Normalize(Vector3.Cross(current, target));
                    current = Vector3.Normalize(Vector3.Transform(current, Quaternion.CreateFromAxisAngle(axis, (float)allowed)));
                }
            }
        }
        return current * (float)settings.FlightSpeed;
    }

    // Top attack (locked target): climb to a cruise altitude above the lock point,
    // then request the configured dive; short-range direct approaches remain explicit.
    internal const int TopAttackClimb = 0, TopAttackCruise = 1, TopAttackDive = 2, TopAttackDirect = 3;
    internal static double TopAttackDiveTrigger(double speed, double turnRateDegrees, AtgmSettings s)
        =>TopAttackDiveDistance(speed,turnRateDegrees,s.TopAttackAltitude,0,s);
    internal static double TopAttackDiveDistance(double speed,double turnRateDegrees,double height,double pitch,AtgmSettings s)
    {
        var dive=Math.Clamp(s.TopAttackDiveAngle,1,89)*Math.PI/180;
        var rate=Math.Max(0,turnRateDegrees)*Math.PI/180;
        if(!double.IsFinite(speed)||speed<=0||rate<=0||!double.IsFinite(height)||!double.IsFinite(pitch))return double.PositiveInfinity;
        pitch=Math.Clamp(pitch,-dive,Math.PI*.49);
        var radius=speed/rate;
        // Current pitch/height matter: a still-climbing missile needs more turn distance.
        var advance=radius*(Math.Sin(pitch)+Math.Sin(dive));
        var afterArcHeight=height+radius*(Math.Cos(dive)-Math.Cos(pitch));
        return Math.Max(0,advance)+Math.Max(0,afterArcHeight)/Math.Tan(dive);
    }
    internal static double TopAttackMinimumRange(double initialHeight,AtgmSettings s,double launchSpeed=0)
    {
        var rate=s.MaxTurnRate*Math.PI/180;
        if(!double.IsFinite(initialHeight)||!double.IsFinite(rate)||rate<=0)return double.PositiveInfinity;
        var radius=s.FlightSpeed/rate;var climb=s.TopAttackClimbAngle*Math.PI/180;
        var firstTurnDistance=radius*Math.Sin(climb);
        var firstTurnHeight=radius*(1-Math.Cos(climb));
        var climbDistance=Math.Max(0,s.TopAttackAltitude-initialHeight-firstTurnHeight)/Math.Tan(climb);
        var diveDistance=TopAttackDiveDistance(s.FlightSpeed,s.MaxTurnRate,s.TopAttackAltitude,climb,s);
        // Conservative full-speed arming travel and one physics-step steering margin.
        return firstTurnDistance+climbDistance+diveDistance+Math.Max(s.FlightSpeed,double.IsFinite(launchSpeed)?launchSpeed:0)*(Math.Max(s.GuidanceDelay,s.MotorDelay)+.05);
    }
    internal static bool TopAttackCanLoft(Vector3 position,Vector3 target,Vector3 heading,AtgmSettings s,double launchSpeed=0)
    {
        var delta=target-position;var flat=new Vector2(delta.X,delta.Z);var distance=flat.Length();
        if(!float.IsFinite(distance)||distance<1e-3f||!float.IsFinite(heading.LengthSquared())||heading.LengthSquared()<1e-8f)return false;
        var forward=Vector3.Dot(Vector3.Normalize(heading),Vector3.Normalize(delta));
        return float.IsFinite(forward)&&forward>=.866f&&distance>=TopAttackMinimumRange(position.Y-target.Y,s,launchSpeed);
    }
    internal static (Vector3 Direction, int Phase)? TopAttackSteer(Vector3 position, Vector3 target, Vector3 heading,
        double speed, int phase, AtgmSettings s)
    {
        static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
        if (!Finite(position) || !Finite(target) || !Finite(heading)) return null;
        if (phase is < TopAttackClimb or > TopAttackDirect) phase = TopAttackClimb;
        var to = target - position;
        var flat = new Vector2(to.X, to.Z);
        var distance = flat.Length();
        var height = position.Y - target.Y;
        if(phase==TopAttackDive && distance>1e-3f && heading.X*to.X+heading.Z*to.Z<0)
            return (Vector3.Normalize(heading),phase);
        var altitude = Math.Max(0, s.TopAttackAltitude);
        var climb = Math.Clamp(s.TopAttackClimbAngle, 0, 89) * Math.PI / 180;
        var dive = Math.Clamp(s.TopAttackDiveAngle, 1, 89) * Math.PI / 180;
        var axis = distance > 1e-3f ? new Vector3(flat.X / distance, 0, flat.Y / distance) : FlatHeading(heading);
        var pitchNow=Math.Asin(Math.Clamp(Vector3.Normalize(heading).Y,-1,1));
        var radius=s.MaxTurnRate>0?speed/(s.MaxTurnRate*Math.PI/180):double.PositiveInfinity;
        // Begin rounding out BEFORE the requested apex. Waiting until reaching it
        // adds an entire turn arc above the ceiling and leaves too little dive room.
        var roundoutHeight=double.IsFinite(radius)?radius*(1-Math.Cos(Math.Max(0,pitchNow))):0;
        if (phase == TopAttackClimb && height+roundoutHeight >= altitude) phase = TopAttackCruise;
        if (phase is TopAttackClimb or TopAttackCruise && distance <= TopAttackDiveDistance(speed,s.MaxTurnRate,height,pitchNow,s)) phase = TopAttackDive;
        Vector3 direction;
        if(phase==TopAttackDirect)direction=to;
        else if (phase == TopAttackClimb)
            direction = axis * (float)Math.Cos(climb) + Vector3.UnitY * (float)Math.Sin(climb);
        else if (phase == TopAttackCruise)
            direction = axis + Vector3.UnitY * (float)Math.Clamp((altitude-height)/Math.Max(50,speed),-.25,.25);
        else
        {
            var pitch = Math.Atan2(height, Math.Max(distance, 1e-4));
            direction = pitch <= 0 || pitch >= dive || pitchNow <= -dive+.035 || distance < speed*.35
                ? to
                : axis * (float)Math.Cos(dive) - Vector3.UnitY * (float)Math.Sin(dive);
        }
        return Finite(direction) && direction.LengthSquared() > 1e-8f
            ? (Vector3.Normalize(direction), phase)
            : null;
    }
    // Estimate only observed motion of the already locked transform; never reacquire.
    internal static Vector3 TopAttackLead(Vector3 target,Vector3 observedVelocity,Vector3 position,double speed)
    {
        if(!float.IsFinite(observedVelocity.LengthSquared())||observedVelocity.LengthSquared()>3600||!double.IsFinite(speed)||speed<=0)return target;
        var horizon=Math.Clamp(Vector3.Distance(target,position)/speed,0,2.5);
        var offset=observedVelocity*(float)horizon;
        if(offset.LengthSquared()>3600)offset=Vector3.Normalize(offset)*60;
        return target+offset;
    }
    internal static double LockDwell(double dwellSeconds, double deltaSeconds)
    {
        var dwell = double.IsFinite(dwellSeconds) && dwellSeconds > 0 ? dwellSeconds : 0;
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0) return dwell;
        return Math.Min(30, dwell + deltaSeconds);
    }
    private static Vector3 FlatHeading(Vector3 heading)
    {
        var flat = new Vector2(heading.X, heading.Z);
        return flat.LengthSquared() > 1e-8f ? new Vector3(flat.X, 0, flat.Y) / flat.Length() : Vector3.UnitZ;
    }
}
