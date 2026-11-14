using Il2CppInterop.Runtime;
using Sprocket.Vehicles.Cannons;
using Sprocket.WeaponFramework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

namespace SprocketExpandedBallistics;

// Visual only: a sealed launch canister on the launcher cradle while the native
// chamber is Loaded; gone while empty or reloading. No collider, mass or crew.
// The canister is NOT parented under the native barrel: firing clones parts of
// that hierarchy (ejection), which duplicated it onto the hull. It follows the
// muzzle frame just before rendering instead, and is an ordinary scene object.
internal static class AtgmLoadedRoundVisual
{
    private sealed record Round(Cannon Cannon, GameObject Root, Transform Barrel, Vector3 Muzzle);
    private static readonly Dictionary<IntPtr, Round> rounds = new();
    private static Material? body, fins, nose;
    private static int warnings;

    private static Material Paint(string name, Color color, float smooth)
    {
        var m = new Material(Shader.Find("HDRP/Lit") ?? throw new InvalidOperationException("HDRP/Lit unavailable")) { name = name, hideFlags = HideFlags.HideAndDontSave };
        m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", smooth); HDMaterial.ValidateMaterial(m); return m;
    }

    private static GameObject Part(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material, int layer)
    {
        var part = GameObject.CreatePrimitive(type);
        var collider = part.GetComponent<Collider>(); if (collider != null) { collider.enabled = false; Object.Destroy(collider); }
        part.layer = layer;
        part.transform.SetParent(parent, false); part.transform.localPosition = position; part.transform.localScale = scale;
        var renderer = part.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.On;
        return part;
    }

    /// <summary>(Re)build the canister: thinner front section, thicker rear motor
    /// section, two clamp bands and black end caps, along the launch axis (+Z).</summary>
    internal static void Build(Cannon cannon, CannonBarrelModel barrel, float tubeLength)
    {
        Remove(cannon.Pointer);
        body ??= Paint("ATGM canister olive", new Color(.22f, .26f, .17f, 1), .3f);
        fins ??= Paint("ATGM canister bands", new Color(.17f, .2f, .13f, 1), .35f);
        nose ??= Paint("ATGM canister caps", new Color(.03f, .03f, .03f, 1), .45f);
        // Fits the cradle clamps (inner width 0.208 m).
        var front = Math.Clamp(cannon.Blueprint.Caliber * .001f * 1.15f, .08f, .17f);
        var rear = Math.Min(front * 1.15f, .2f);
        var length = Math.Max(.3f, tubeLength - .02f);
        var layer = barrel.gameObject.layer;
        Install();
        var root = new GameObject("ATGM launch canister") { layer = layer };
        Follow(root.transform, barrel.transform, barrel.FireLocalPosition);
        GameObject Cylinder(float diameter, float z0, float z1, Material material)
        {
            var c = Part(PrimitiveType.Cylinder, root.transform, new Vector3(0, 0, (z0 + z1) / 2), new Vector3(diameter, (z1 - z0) / 2, diameter), material, layer);
            c.transform.localRotation = Quaternion.Euler(90, 0, 0);
            return c;
        }
        float tail = -length, split = -length * .38f;
        Cylinder(front, split, -.025f, body);                       // front tube
        Cylinder(rear, tail + .025f, split, body);                  // motor section
        Cylinder(front * 1.06f, -.025f, 0f, nose);                  // front cap
        Cylinder(rear * 1.04f, tail, tail + .025f, nose);           // rear cap
        Cylinder(rear * 1.05f, split - .02f, split + .02f, fins);   // section joint
        foreach (var z in new[] { -length * .18f, -length * .62f })
            Cylinder((z > split ? front : rear) * 1.05f, z - .015f, z + .015f, fins);
        rounds[cannon.Pointer] = new(cannon, root, barrel.transform, barrel.FireLocalPosition);
        root.SetActive(Loaded(cannon));
    }

    // Editor (no live weapon) shows a loaded launcher; in battle follow the chamber.
    private static bool Loaded(Cannon cannon)
    {
        var weapon = cannon.Behaviour?.TryCast<WeaponBehaviour>();
        if (weapon == null) return true;
        return weapon.Functioning && weapon.LoadState.ToString() == "Loaded";
    }

    internal static void Update()
    {
        if (rounds.Count == 0) return;
        foreach (var pair in rounds.ToArray())
        {
            try
            {
                var round = pair.Value;
                if (round.Root == null || round.Barrel == null || round.Cannon == null || round.Cannon.WasCollected) { Remove(pair.Key); continue; }
                var loaded = Loaded(round.Cannon);
                if (round.Root.activeSelf != loaded) round.Root.SetActive(loaded);
            }
            catch (Exception ex)
            {
                Remove(pair.Key);
                if (warnings++ < 3) Plugin.ModLog.LogWarning("[ATGM launcher] Loaded round visual: " + ex.Message);
            }
        }
    }

    private static bool installed;
    private static void Install()
    {
        if (installed) return;
        installed = true;
        Application.add_onBeforeRender(DelegateSupport.ConvertDelegate<UnityAction>((Action)BeforeRender));
    }

    private static void Follow(Transform root, Transform barrel, Vector3 muzzle)
    {
        root.SetPositionAndRotation(barrel.TransformPoint(muzzle), barrel.rotation);
    }

    // After all native animation (traverse, elevation, recoil) for this frame.
    private static void BeforeRender()
    {
        if (rounds.Count == 0) return;
        foreach (var pair in rounds.ToArray())
        {
            try
            {
                var round = pair.Value;
                if (round.Root == null || round.Barrel == null) { Remove(pair.Key); continue; }
                if (round.Root.activeSelf) Follow(round.Root.transform, round.Barrel, round.Muzzle);
            }
            catch { Remove(pair.Key); }
        }
    }

    internal static void Remove(IntPtr cannon)
    {
        if (rounds.Remove(cannon, out var round) && round.Root != null) Object.Destroy(round.Root);
    }

    internal static void Clear()
    {
        foreach (var key in rounds.Keys.ToArray()) Remove(key);
        foreach (var m in new[] { body, fins, nose }) if (m != null) Object.Destroy(m);
        body = fins = nose = null;
    }
}
