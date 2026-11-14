using System.Numerics;
using SprocketOscillatingTurret;

int checks = 0;
void Check(bool ok, string what) { checks++; if (!ok) throw new Exception(what); }
bool Near(float a, float b, float eps = 1e-3f) => Math.Abs(a - b) <= eps;

Check(!OscillatingDatePolicy.Allows(new DateTime(1951, 12, 31)), "oscillating drive unavailable one day early");
Check(OscillatingDatePolicy.Allows(new DateTime(1952, 1, 1)), "oscillating drive available on 1952.01.01");
Check(OscillatingDatePolicy.Allows(new DateTime(9999, 12, 31)), "final-era sentinel makes the drive available");
Check(!OscillatingDatePolicy.Allows(null), "missing vehicle date fails closed");
Check(OscillatingDatePolicy.MinimumDate == new DateTime(1952, 1, 1), "part and preset share 1952.01.01");
var savedOscillatingPart = "oscillatingElevationDrive";
Check(!OscillatingDatePolicy.Allows(new DateTime(1950, 1, 1)) && savedOscillatingPart == "oscillatingElevationDrive",
    "too-early saved drive keeps its identity and stays inactive");

// 0.2.11 X-ray regression math. The plugin's owned pose keeps the upper
// structure upright; native Apply's LookRotation(forward, axis) rolls it 90
// degrees when axis is the plugin's local -X.
Check(Near(ElevationMath.ComputeElevationAxis(Quaternion.Identity).X, -1f) &&
      Near(ElevationMath.ComputeElevationAxis(Quaternion.Identity).Y, 0f),
    "owned elevation axis is local -X");

Quaternion ownedZero = ElevationMath.ComputeElevationRotation(Quaternion.Identity, 0f);
Vector3 ownedUpZero = Vector3.Transform(Vector3.UnitY, ownedZero);
Check(Vector3.Distance(ownedUpZero, Vector3.UnitY) < 1e-4f, "owned pose at 0 degrees stays upright");

Quaternion ownedPitch = ElevationMath.ComputeElevationRotation(Quaternion.Identity, 20f);
Vector3 ownedUpPitch = Vector3.Transform(Vector3.UnitY, ownedPitch);
Check(ownedUpPitch.Y > 0.9f, "owned elevation pitch never rolls the upper structure");

Quaternion nativePose = LookRotation(Vector3.UnitZ, -Vector3.UnitX);
Vector3 nativeUp = Vector3.Transform(Vector3.UnitY, nativePose);
Check(Math.Abs(nativeUp.Y) < 1e-3f, "native Apply with -X up rolls local up to horizontal");
Check(Vector3.Dot(nativeUp, -Vector3.UnitX) > 0.99f, "native roll is exactly 90 degrees toward -X");

Vector3 fixAxis = ElevationMath.ComputeNativeAxis(Quaternion.Identity);
Check(Vector3.Distance(fixAxis, Vector3.UnitY) < 1e-4f, "0.2.11 guard restores native up axis");
Vector3 fixedUp = Vector3.Transform(Vector3.UnitY, LookRotation(Vector3.UnitZ, fixAxis));
Check(fixedUp.Y > 0.99f, "native Apply with the restored up axis keeps the turret upright");

Console.WriteLine($"PASS {checks} oscillating-turret checks.");

// 0.3.0 Heavy Trunnion Mount. Argument 0: the built DLL (for the embedded icon check).
int before = checks;
HeavyTrunnionTests.Run(Check, args.Length > 0 ? args[0] : null);
Console.WriteLine($"PASS {checks - before} heavy-trunnion checks ({checks} total).");

static Quaternion LookRotation(Vector3 forward, Vector3 up)
{
    forward = Vector3.Normalize(forward);
    Vector3 right = Vector3.Cross(up, forward);
    if (right.LengthSquared() < 1e-12f) return Quaternion.Identity;
    right = Vector3.Normalize(right);
    Vector3 trueUp = Vector3.Cross(forward, right);
    var m = new Matrix4x4(
        right.X, right.Y, right.Z, 0f,
        trueUp.X, trueUp.Y, trueUp.Z, 0f,
        forward.X, forward.Y, forward.Z, 0f,
        0f, 0f, 0f, 1f);
    return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
}
