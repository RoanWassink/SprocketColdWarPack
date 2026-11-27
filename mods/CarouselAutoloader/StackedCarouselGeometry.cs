using System;
using System.Collections.Generic;
using System.Linq;

namespace SprocketCarouselAutoloader;

// Pure procedural geometry for the 2S38-style stacked unitary carousel (no Unity, Harmony or Il2Cpp).
// CarouselVisualGeometry.cs stays byte-identical to 0.2.28 for T-72/T-80 Simple and damage meshes.
public static class StackedCarouselGeometry
{
    private static readonly CarouselVisualVector Up = new(0, 1, 0);
    public const int MaximumSlots = CarouselGeometry.MaximumStackedCapacity;

    /// <summary>Visible slot ranges for a stacked multi-tier carousel (bottom tier = 0, top tier fired first).</summary>
    public static IReadOnlyList<(int Start, int Count)> VisibleRanges(int capacity, int slotsPerTier, int liftSlot, int stock)
    {
        if (capacity <= 0 || slotsPerTier <= 0) return Array.Empty<(int, int)>();
        int s = Math.Clamp(stock, 0, capacity);
        if (s == 0) return Array.Empty<(int, int)>();
        int fullTiers = (s - 1) / slotsPerTier;
        int offset = fullTiers * slotsPerTier;
        int remInTier = s - offset;
        int tierCapacity = Math.Min(slotsPerTier, capacity - offset);
        var result = new List<(int Start, int Count)>(3);
        if (offset > 0) result.Add((0, offset));
        if (tierCapacity == slotsPerTier)
        {
            foreach (var (start, count) in CarouselModelGeometry.RingVisibleRanges(slotsPerTier, liftSlot, remInTier))
                result.Add((offset + start, count));
        }
        else
        {
            result.Add((offset, remInTier));
        }
        return result;
    }

    // All dimensions in metres except capacity and slotsPerTier. Origin is basket centre-bottom; Y up, Z forward.
    public static IReadOnlyList<CarouselVisualMesh> Build(double diameter, double depth, int capacity, int slotsPerTier,
        double calibre, double roundLength, int visibleRounds = -1, CarouselLiftTarget? target = null, bool showLift = true)
    {
        foreach (var x in new[] { diameter, depth, calibre, roundLength })
            if (!double.IsFinite(x) || x <= 0) throw new ArgumentOutOfRangeException("dimensions");
        if (capacity < 0 || capacity > MaximumSlots) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (visibleRounds < 0) visibleRounds = capacity;
        visibleRounds = Math.Clamp(visibleRounds, 0, capacity);
        var r = diameter / 2;
        if (r <= .08 || depth < .06) throw new ArgumentOutOfRangeException("basket");

        var output = new List<CarouselVisualMesh>();
        var frame = new CarouselVisualMesh("mechanism", "Mechanism"); output.Add(frame);
        var trim = new CarouselVisualMesh("drive_trim", "DriveTrim"); output.Add(trim);

        var floor = Math.Min(.055, depth * .12);
        var baseY = Math.Max(floor, CarouselGeometry.StackedBaseDepthMm / 1000.0);
        var tierHeight = CarouselGeometry.StackedTierHeightMm(calibre * 1000.0) / 1000.0;
        var hubRadius = Math.Min(r * .16, Math.Max(.09, r * .12));

        int perTier = capacity > 0 ? Math.Max(1, slotsPerTier > 0 ? slotsPerTier : capacity) : 0;
        int tiers = capacity > 0 ? (capacity + perTier - 1) / perTier : 0;
        double stackTop = tiers > 0 ? baseY + tiers * tierHeight : Math.Min(.145, depth * .33);

        // Stationary floor plate, outer rim and central drive column.
        Ring(frame, hubRadius, r - .025, .015, floor, 36);
        Ring(frame, r - .055, r - .025, floor, Math.Min(.115, Math.Max(floor + .01, baseY)), 36);
        Cylinder(frame, new(0, floor, 0), new(0, Math.Min(depth, Math.Max(.145, stackTop)), 0), hubRadius, hubRadius * .88, 16);
        Cylinder(trim, new(0, floor, 0), new(0, floor + .014, 0), hubRadius * 1.08, hubRadius * 1.08, 16);
        if (tiers > 0 && stackTop <= depth + .001)
            Cylinder(trim, new(0, Math.Max(floor + .02, stackTop - .014), 0), new(0, stackTop, 0), hubRadius * 1.02, hubRadius * 1.02, 16);

        if (capacity == 0) return Combine(output);

        var width = calibre * 1.18 + .008;
        var end = Math.Sqrt(Math.Max(0, (r - .04) * (r - .04) - Math.Pow(width / 2, 2)));
        if (roundLength > end - hubRadius)
            throw new ArgumentOutOfRangeException("allocation", "Complete round length must fit the supplied basket.");

        double liftAngle = target is { } t ? CarouselModelGeometry.LiftDirection(t.X, t.Z) : Math.PI;
        double rot = CarouselModelGeometry.RingRotation(perTier, liftAngle);

        // Rotating drum skeleton: thin horizontal tier separator rings + outer divider stanchions.
        var cage = new CarouselVisualMesh("cassette_drum", "Cassette"); output.Add(cage);
        double innerRing = Math.Max(hubRadius + .012, end - roundLength);
        int ringSegs = 32;
        for (int tier = 0; tier < tiers; tier++)
        {
            double ty = baseY + tier * tierHeight;
            Ring(cage, innerRing, end, ty, ty + .005, ringSegs);
        }
        int stanchionStep = perTier > 32 ? 2 : 1;
        double stanchionH = Math.Max(.02, tiers * tierHeight);
        double stanchionMidY = baseY + stanchionH * .5;
        for (int s = 0; s < perTier; s += stanchionStep)
        {
            double angle = CarouselModelGeometry.SlotAngle(perTier, s, rot) + Math.PI / perTier;
            var radial = new CarouselVisualVector(Math.Sin(angle), 0, Math.Cos(angle));
            var tangent = new CarouselVisualVector(Math.Cos(angle), 0, -Math.Sin(angle));
            Box(cage, radial * (end - .008) + Up * stanchionMidY, tangent, Up, radial, .008, stanchionH, .012);
        }

        // Stacked unitary complete rounds: bottom tier 0 up to top tier (tiers - 1).
        double radius = calibre * .5;
        double caseRadius = calibre * .52;
        double noseLen = Math.Min(roundLength * .24, calibre * 1.4);
        double caseLen = Math.Max(roundLength * .55, roundLength - noseLen - calibre * 1.2);
        caseLen = Math.Clamp(caseLen, roundLength * .25, Math.Max(roundLength * .25, roundLength - noseLen));

        for (int slot = 0; slot < capacity; slot++)
        {
            if (slot >= visibleRounds) continue;
            int tier = slot / perTier;
            int ringSlot = slot % perTier;
            double angle = CarouselModelGeometry.SlotAngle(perTier, ringSlot, rot);
            var radial = new CarouselVisualVector(Math.Sin(angle), 0, Math.Cos(angle));
            double py = baseY + tier * tierHeight + tierHeight * .5;

            var round = new CarouselVisualMesh($"projectile_{slot:000}", "Projectile", slot); output.Add(round);
            var tip = radial * (end - roundLength) + Up * py;
            var shoulder = radial * (end - roundLength + noseLen) + Up * py;
            var caseFront = radial * (end - caseLen) + Up * py;
            var rim = radial * end + Up * py;

            // Nose cone + projectile body + cartridge case (6-sided for compact triangle budget at up to 256 rounds).
            Cylinder(round, tip, shoulder, radius * .16, radius, 6);
            if ((caseFront - shoulder).Length > 1e-4)
                Cylinder(round, shoulder, caseFront, radius, caseRadius, 6);
            Cylinder(round, caseFront, rim, caseRadius, caseRadius, 6);
        }

        // Stationary 2S38-style vertical feeder / elevator chute on the rim behind the cannon.
        if (showLift)
        {
            var outward = new CarouselVisualVector(Math.Sin(liftAngle), 0, Math.Cos(liftAngle));
            var side = new CarouselVisualVector(Math.Cos(liftAngle), 0, -Math.Sin(liftAngle));
            double liftTop = target is { } lt && double.IsFinite(lt.Y)
                ? Math.Clamp(lt.Y, stackTop + .04, stackTop + .45)
                : stackTop + Math.Clamp(calibre * 2.2, .10, .28);
            double liftH = Math.Max(.08, liftTop - floor);
            double liftMidY = floor + liftH * .5;
            double chuteW = Math.Clamp(width * 1.1, .05, .26);
            double chuteD = Math.Clamp(calibre * .9, .04, .18);
            var chuteBase = outward * (end + chuteD * .4);
            foreach (var s in new[] { -1, 1 })
                Box(frame, chuteBase + side * (s * chuteW * .46) + Up * liftMidY, side, Up, outward, .014, liftH, chuteD);
            Box(frame, chuteBase + outward * (chuteD * .45) + Up * liftMidY, side, Up, outward, chuteW, liftH, .014);
            Box(frame, chuteBase - outward * (chuteD * .3) + Up * liftTop, side, Up, outward, chuteW * 1.05, .022, chuteD * 1.6);
        }

        if (output.Where(m => m.Material != "Mechanism" && m.Material != "DriveTrim").SelectMany(m => m.Vertices).Any(p => p.Y > depth + .002))
            throw new ArgumentOutOfRangeException("depth", "Stacked ammunition exceeds the basket depth.");

        return Combine(output);
    }

    private static IReadOnlyList<CarouselVisualMesh> Combine(List<CarouselVisualMesh> source)
    {
        var result = new List<CarouselVisualMesh>();
        foreach (var group in source.GroupBy(m => m.Material))
        {
            var mesh = new CarouselVisualMesh(group.Key.ToLowerInvariant(), group.Key);
            var prefix = new List<int> { 0 };
            foreach (var part in group)
            {
                var offset = mesh.Vertices.Count;
                mesh.Vertices.AddRange(part.Vertices); mesh.Normals.AddRange(part.Normals);
                mesh.Triangles.AddRange(part.Triangles.Select(i => i + offset));
                if (part.Slot >= 0) prefix.Add(mesh.Triangles.Count);
            }
            if (prefix.Count > 1) mesh.SlotPrefixIndexCounts = prefix.ToArray();
            result.Add(mesh);
        }
        return result;
    }

    private static void Cylinder(CarouselVisualMesh m, CarouselVisualVector a, CarouselVisualVector b, double ra, double rb, int n)
    {
        var axis = (b - a).Unit;
        var u = CarouselVisualVector.Cross(Math.Abs(axis.Y) > .9 ? new(1, 0, 0) : Up, axis).Unit;
        var v = CarouselVisualVector.Cross(axis, u);
        for (var i = 0; i < n; i++)
        {
            var t = i * 2 * Math.PI / n; var q = (i + 1) * 2 * Math.PI / n;
            var d = u * Math.Cos(t) + v * Math.Sin(t); var e = u * Math.Cos(q) + v * Math.Sin(q);
            m.Quad(a + d * ra, a + e * ra, b + e * rb, b + d * rb);
            m.Triangle(a, a + e * ra, a + d * ra); m.Triangle(b, b + d * rb, b + e * rb);
        }
    }

    private static void Ring(CarouselVisualMesh m, double inner, double outer, double bottom, double top, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var t = i * 2 * Math.PI / n; var q = (i + 1) * 2 * Math.PI / n;
            CarouselVisualVector P(double radius, double y, double angle) => new(Math.Sin(angle) * radius, y, Math.Cos(angle) * radius);
            var a = P(inner, bottom, t); var b = P(outer, bottom, t); var c = P(outer, bottom, q); var d = P(inner, bottom, q);
            var aa = P(inner, top, t); var bb = P(outer, top, t); var cc = P(outer, top, q); var dd = P(inner, top, q);
            m.Quad(aa, bb, cc, dd); m.Quad(a, d, c, b); m.Quad(b, c, cc, bb); m.Quad(a, aa, dd, d);
        }
    }

    private static void Box(CarouselVisualMesh m, CarouselVisualVector center, CarouselVisualVector x, CarouselVisualVector y, CarouselVisualVector z, double sx, double sy, double sz)
    {
        CarouselVisualVector P(int a, int b, int c) => center + x * (a * sx / 2) + y * (b * sy / 2) + z * (c * sz / 2);
        m.Quad(P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1));
        m.Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));
        m.Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1));
        m.Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1));
        m.Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1));
        m.Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1));
    }
}
