using CMS.Core.Onvif;

namespace CMS.Core.Attendance;

/// <summary>
/// Turns a requested arc in degrees into the absolute positions the camera will
/// visit.
///
/// Degrees have to be converted through the node's own pan range, because ONVIF
/// normalises pan to -1..1 across whatever the camera can mechanically do. A
/// camera that pans 360 degrees and one that pans 100 both report the same
/// normalised limits, so a sweep expressed in normalised units would cover a
/// completely different arc on each.
/// </summary>
public static class SweepPlanner
{
    /// <summary>
    /// Builds a plan centred on the camera's current position. Returns a plan
    /// with no stops when the camera cannot be positioned absolutely, which the
    /// caller must treat as a refusal rather than an empty sweep.
    /// </summary>
    public static SweepPlan Plan(PtzNodeInfo node, PtzPosition current, SweepOptions options)
    {
        var plan = new SweepPlan
        {
            ArcDegrees = options.ArcDegrees,
            FieldOfViewDegrees = options.FieldOfViewDegrees,
            OverlapFraction = MathEx.Clamp(options.OverlapFraction, 0f, 0.9f),
            OriginalPosition = current
        };

        if (node == null || !node.SupportsAbsoluteMove)
        {
            return plan;
        }

        var unitsPerDegree = node.UnitsPerDegree;
        if (unitsPerDegree <= 0f)
        {
            return plan;
        }

        // Each stop advances by the unobscured part of the field of view, so
        // consecutive tiles overlap and nobody falls into a seam.
        var step = options.FieldOfViewDegrees * (1f - plan.OverlapFraction);
        if (step < 1f)
        {
            step = 1f;
        }

        plan.StepDegrees = step;

        var arc = Math.Max(0f, options.ArcDegrees);
        var stopCount = (int)Math.Floor(arc / step) + 1;
        if (stopCount < 1)
        {
            stopCount = 1;
        }

        // Centre the arc on where the camera already points, so the operator
        // frames the room and the sweep covers it symmetrically.
        var half = arc / 2f;
        var zoom = MathEx.Clamp(options.Zoom, node.ZoomMin, node.ZoomMax);

        for (var i = 0; i < stopCount; i++)
        {
            var offset = -half + (i * step);
            if (offset > half)
            {
                offset = half;
            }

            var pan = current.Pan + (offset * unitsPerDegree);

            // A camera that cannot turn far enough simply stops at its limit.
            // Emitting a position beyond it would leave the sweep waiting for an
            // arrival that never happens.
            if (pan < node.PanMin || pan > node.PanMax)
            {
                if (!WrapsFullCircle(node))
                {
                    pan = MathEx.Clamp(pan, node.PanMin, node.PanMax);
                }
                else
                {
                    pan = Wrap(pan, node.PanMin, node.PanMax);
                }
            }

            plan.Stops.Add(new SweepStop(i, offset, new PtzPosition(pan, current.Tilt, zoom)));
        }

        RemoveDuplicateStops(plan);
        return plan;
    }

    /// <summary>
    /// Clamping can collapse several requested stops onto the same limit. Those
    /// duplicates would photograph the same view repeatedly and inflate the
    /// capture count for no benefit.
    /// </summary>
    private static void RemoveDuplicateStops(SweepPlan plan)
    {
        var kept = new List<SweepStop>();

        foreach (var stop in plan.Stops)
        {
            var duplicate = kept.Any(k => Math.Abs(k.Position.Pan - stop.Position.Pan) < 1e-4f);
            if (!duplicate)
            {
                kept.Add(stop);
            }
        }

        plan.Stops.Clear();

        for (var i = 0; i < kept.Count; i++)
        {
            plan.Stops.Add(new SweepStop(i, kept[i].OffsetDegrees, kept[i].Position));
        }
    }

    /// <summary>A node spanning the whole normalised range can turn continuously.</summary>
    private static bool WrapsFullCircle(PtzNodeInfo node)
        => node.PanMin <= -0.999f && node.PanMax >= 0.999f && node.PanRangeDegrees >= 359f;

    private static float Wrap(float value, float min, float max)
    {
        var span = max - min;
        if (span <= 0f)
        {
            return value;
        }

        while (value < min)
        {
            value += span;
        }

        while (value > max)
        {
            value -= span;
        }

        return value;
    }
}
