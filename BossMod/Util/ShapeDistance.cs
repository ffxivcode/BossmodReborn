﻿namespace BossMod;

// shapes can be defined by distance from point to shape's border; distance is positive for points outside shape and negative for points inside shape
// union is min, intersection is max

public static class ShapeDistance
{
    // for concave polygons this seems to provide a big speed up (seen 10x uplift), for others not so much or even detrimental. not sure why.
    public static Func<WPos, float> CacheFunction(Func<WPos, float> func)
    {
        var cache = new ConcurrentDictionary<WPos, float>();
        return p =>
        {
            if (cache.TryGetValue(p, out var cachedValue))
                return cachedValue;
            var result = func(p);
            cache[p] = result;
            return result;
        };
    }

    public static Func<WPos, float> HalfPlane(WPos point, WDir normal) => p => normal.Dot(p - point);

    public static Func<WPos, float> Circle(WPos origin, float radius) => radius <= 0 ? (_ => float.MaxValue) : (p => (p - origin).Length() - radius);
    public static Func<WPos, float> InvertedCircle(WPos origin, float radius) => radius <= 0 ? (_ => float.MinValue) : (p => radius - (p - origin).Length());

    public static Func<WPos, float> Donut(WPos origin, float innerRadius, float outerRadius)
    {
        return outerRadius <= 0 || innerRadius >= outerRadius ? (_ => float.MaxValue) : innerRadius <= 0 ? Circle(origin, outerRadius) : (p =>
        {
            // intersection of outer circle and inverted inner circle
            var distOrigin = (p - origin).Length();
            var distOuter = distOrigin - outerRadius;
            var distInner = innerRadius - distOrigin;
            return Math.Max(distOuter, distInner);
        });
    }

    public static Func<WPos, float> InvertedDonut(WPos origin, float innerRadius, float outerRadius)
    {
        var donut = Donut(origin, innerRadius, outerRadius);
        return p => -donut(p);
    }

    public static Func<WPos, float> Cone(WPos origin, float radius, Angle centerDir, Angle halfAngle)
    {
        if (halfAngle.Rad <= 0 || radius <= 0)
            return _ => float.MaxValue;
        if (halfAngle.Rad >= MathF.PI)
            return Circle(origin, radius);
        // for <= 180-degree cone: result = intersection of circle and two half-planes with normals pointing outside cone sides
        // for > 180-degree cone: result = intersection of circle and negated intersection of two half-planes with inverted normals
        // both normals point outside
        float coneFactor = halfAngle.Rad > MathF.PI / 2 ? -1 : 1;
        var nl = coneFactor * (centerDir + halfAngle).ToDirection().OrthoL();
        var nr = coneFactor * (centerDir - halfAngle).ToDirection().OrthoR();
        return p =>
        {
            var off = p - origin;
            var distOrigin = off.Length();
            var distOuter = distOrigin - radius;
            var distLeft = off.Dot(nl);
            var distRight = off.Dot(nr);
            return Math.Max(distOuter, coneFactor * Math.Max(distLeft, distRight));
        };
    }

    public static Func<WPos, float> InvertedCone(WPos origin, float radius, Angle centerDir, Angle halfAngle)
    {
        var cone = Cone(origin, radius, centerDir, halfAngle);
        return p => -cone(p);
    }

    public static Func<WPos, float> DonutSector(WPos origin, float innerRadius, float outerRadius, Angle centerDir, Angle halfAngle)
    {
        if (halfAngle.Rad <= 0 || outerRadius <= 0 || innerRadius >= outerRadius)
            return _ => float.MaxValue;
        if (halfAngle.Rad >= MathF.PI)
            return Donut(origin, innerRadius, outerRadius);
        if (innerRadius <= 0)
            return Cone(origin, outerRadius, centerDir, halfAngle);
        float coneFactor = halfAngle.Rad > MathF.PI / 2 ? -1 : 1;
        var nl = coneFactor * (centerDir + halfAngle + 90.Degrees()).ToDirection();
        var nr = coneFactor * (centerDir - halfAngle - 90.Degrees()).ToDirection();
        return p =>
        {
            var off = p - origin;
            var distOrigin = off.Length();
            var distOuter = distOrigin - outerRadius;
            var distInner = innerRadius - distOrigin;
            var distLeft = off.Dot(nl);
            var distRight = off.Dot(nr);
            return Math.Max(Math.Max(distOuter, distInner), coneFactor * Math.Max(distLeft, distRight));
        };
    }

    public static Func<WPos, float> InvertedDonutSector(WPos origin, float innerRadius, float outerRadius, Angle centerDir, Angle halfAngle)
    {
        var donutSectir = DonutSector(origin, innerRadius, outerRadius, centerDir, halfAngle);
        return p => -donutSectir(p);
    }

    public static Func<WPos, float> Tri(WPos origin, RelTriangle tri)
    {
        var ab = tri.B - tri.A;
        var bc = tri.C - tri.B;
        var ca = tri.A - tri.C;
        var n1 = ab.OrthoL().Normalized();
        var n2 = bc.OrthoL().Normalized();
        var n3 = ca.OrthoL().Normalized();
        if (ab.Cross(bc) < 0)
        {
            n1 = -n1;
            n2 = -n2;
            n3 = -n3;
        }
        var a = origin + tri.A;
        var b = origin + tri.B;
        var c = origin + tri.C;
        return p =>
        {
            var d1 = n1.Dot(p - a);
            var d2 = n2.Dot(p - b);
            var d3 = n3.Dot(p - c);
            return Math.Max(Math.Max(d1, d2), d3);
        };
    }

    public static Func<WPos, float> InvertedTri(WPos origin, RelTriangle tri)
    {
        var triangle = Tri(origin, tri);
        return p => -triangle(p);
    }

    public static Func<WPos, float> TriList(WPos origin, List<RelTriangle> tris) => Union([.. tris.Select(tri => Tri(origin, tri))]);

    public static Func<WPos, float> Rect(WPos origin, WDir dir, float lenFront, float lenBack, float halfWidth)
    {
        // dir points outside far side
        var normal = dir.OrthoL(); // points outside left side
        return p =>
        {
            var offset = p - origin;
            var distParr = offset.Dot(dir);
            var distOrtho = offset.Dot(normal);
            var distFront = distParr - lenFront;
            var distBack = -distParr - lenBack;
            var distLeft = distOrtho - halfWidth;
            var distRight = -distOrtho - halfWidth;
            return Math.Max(Math.Max(distFront, distBack), Math.Max(distLeft, distRight));
        };
    }
    public static Func<WPos, float> Rect(WPos origin, Angle direction, float lenFront, float lenBack, float halfWidth) => Rect(origin, direction.ToDirection(), lenFront, lenBack, halfWidth);
    public static Func<WPos, float> Rect(WPos from, WPos to, float halfWidth)
    {
        var dir = to - from;
        var l = dir.Length();
        return Rect(from, dir / l, l, 0, halfWidth);
    }

    public static Func<WPos, float> InvertedRect(WPos origin, WDir dir, float lenFront, float lenBack, float halfWidth)
    {
        var rect = Rect(origin, dir, lenFront, lenBack, halfWidth);
        return p => -rect(p);
    }
    public static Func<WPos, float> InvertedRect(WPos origin, Angle direction, float lenFront, float lenBack, float halfWidth) => InvertedRect(origin, direction.ToDirection(), lenFront, lenBack, halfWidth);
    public static Func<WPos, float> InvertedRect(WPos from, WPos to, float halfWidth)
    {
        var dir = to - from;
        var l = dir.Length();
        return InvertedRect(from, dir / l, l, 0, halfWidth);
    }

    public static Func<WPos, float> Cross(WPos origin, Angle direction, float length, float halfWidth)
    {
        var dir = direction.ToDirection();
        var normal = dir.OrthoL();
        return p =>
        {
            var offset = p - origin;
            var distParr = offset.Dot(dir);
            var distOrtho = offset.Dot(normal);
            var distPFront = distParr - length;
            var distPBack = -distParr - length;
            var distPLeft = distOrtho - halfWidth;
            var distPRight = -distOrtho - halfWidth;
            var distOFront = distOrtho - length;
            var distOBack = -distOrtho - length;
            var distOLeft = distParr - halfWidth;
            var distORight = -distParr - halfWidth;
            var distP = Math.Max(Math.Max(distPFront, distPBack), Math.Max(distPLeft, distPRight));
            var distO = Math.Max(Math.Max(distOFront, distOBack), Math.Max(distOLeft, distORight));
            return Math.Min(distP, distO);
        };
    }

    public static Func<WPos, float> InvertedCross(WPos origin, Angle direction, float length, float halfWidth)
    {
        var cross = Cross(origin, direction, length, halfWidth);
        return p => -cross(p);
    }

    // positive offset increases area
    public static Func<WPos, float> ConvexPolygon(IEnumerable<(WPos, WPos)> edges, bool cw, float offset = 0)
    {
        Func<WPos, float> edge((WPos p1, WPos p2) e)
        {
            if (e.p1 == e.p2)
                return CacheFunction(_ => float.MinValue);
            var dir = (e.p2 - e.p1).Normalized();
            var normal = cw ? dir.OrthoL() : dir.OrthoR();
            return CacheFunction(p => normal.Dot(p - e.p1));
        }
        return CacheFunction(Intersection([.. edges.Select(edge)], offset));
    }

    public static Func<WPos, float> ConvexPolygon(IEnumerable<WPos> vertices, bool cw, float offset = 0) => ConvexPolygon(PolygonUtil.EnumerateEdges(vertices), cw, offset);

    public static Func<WPos, float> InvertedConvexPolygon(IEnumerable<WPos> vertices, bool cw, float offset = 0)
    {
        var convexPolygon = ConvexPolygon(vertices, cw, offset);
        return p => -convexPolygon(p);
    }

    public static Func<WPos, float> ConcavePolygon(IEnumerable<WPos> vertices)
    {
        var edges = PolygonUtil.EnumerateEdges(vertices).ToList();
        return CacheFunction(p =>
        {
            var isInside = PolygonUtil.IsPointInsideConcavePolygon(p, vertices);
            var minDistance = edges.Min(e => PolygonUtil.DistanceToEdge(p, e));
            return isInside ? -minDistance : minDistance;
        });
    }

    public static Func<WPos, float> InvertedConcavePolygon(IEnumerable<WPos> vertices)
    {
        var concavePolygon = ConcavePolygon(vertices);
        return p => -concavePolygon(p);
    }

    private static Func<WPos, float> Intersection(List<Func<WPos, float>> funcs, float offset = 0) => p => funcs.Max(e => e(p)) - offset;
    private static Func<WPos, float> Union(List<Func<WPos, float>> funcs, float offset = 0) => p => funcs.Min(e => e(p)) - offset;

}
