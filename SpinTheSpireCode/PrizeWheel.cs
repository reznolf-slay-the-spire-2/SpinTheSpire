using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Random;

namespace SpinTheSpire.SpinTheSpireCode;

/// <summary>
/// A Wheel-of-Names style prize wheel. The wheel itself is drawn procedurally so the first
/// version needs no PCK or external art; the labels/icons use game fonts and colors.
/// </summary>
public sealed partial class PrizeWheel : Control
{
    public sealed class Segment
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public Color Color { get; init; } = Colors.White;
        public int Weight { get; init; } = 1;
    }

    private readonly List<Segment> _segments = new();
    private Label _titleLabel = null!;
    private Label _resultLabel = null!;
    private PrizeWheelSpinner _spinner = null!;
    private PrizeWheelPointer _pointer = null!;
    private float _diameter = 400f;
    private Tween? _spinTween;
    private TaskCompletionSource<bool>? _spinDone;

    public float Diameter => _diameter;
    public IReadOnlyList<Segment> Segments => _segments;

    public override void _Ready()
    {
        try
        {
            BuildUi();
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not build prize wheel: {e}");
        }
    }

    private void BuildUi()
    {
        var vbox = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        vbox.AddThemeConstantOverride("separation", 2);
        AddChild(vbox);

        _titleLabel = SpinUi.MakeLabel("", 24, SpinUi.Gold, bold: true, HorizontalAlignment.Center);
        _titleLabel.CustomMinimumSize = new Vector2(0, 36);
        vbox.AddChild(_titleLabel);

        var center = new CenterContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        vbox.AddChild(center);

        var holder = new Control
        {
            CustomMinimumSize = new Vector2(_diameter + 26f, _diameter + 8f),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        center.AddChild(holder);

        _spinner = new PrizeWheelSpinner
        {
            Position = Vector2.Zero,
            Size = new Vector2(_diameter, _diameter),
            PivotOffset = new Vector2(_diameter * 0.5f, _diameter * 0.5f)
        };
        holder.AddChild(_spinner);

        _pointer = new PrizeWheelPointer
        {
            WheelDiameter = _diameter,
            MouseFilter = MouseFilterEnum.Ignore
        };
        SpinUi.SetFullRect(_pointer);
        holder.AddChild(_pointer);

        _resultLabel = SpinUi.MakeLabel("", 30, SpinUi.Cream, bold: true, HorizontalAlignment.Center);
        _resultLabel.CustomMinimumSize = new Vector2(0, 48);
        vbox.AddChild(_resultLabel);

        SpinUi.SetFullRect(vbox);
    }

    public void Configure(string title, IEnumerable<Segment> segments, float diameter)
    {
        _diameter = diameter;
        _segments.Clear();
        _segments.AddRange(segments.Where(s => s.Weight > 0));
        if (_segments.Count == 0)
        {
            _segments.Add(new Segment { Key = "EMPTY", Label = "-", Color = Colors.Gray });
        }

        CustomMinimumSize = new Vector2(diameter + 40f, diameter + 112f);

        if (!IsInstanceValid(_titleLabel))
        {
            return;
        }

        _titleLabel.Text = title;
        _titleLabel.Visible = !string.IsNullOrWhiteSpace(title);
        _resultLabel.Text = "";
        _resultLabel.Modulate = Colors.White;

        if (IsInstanceValid(_spinner))
        {
            _spinner.Position = Vector2.Zero;
            _spinner.Size = new Vector2(diameter, diameter);
            _spinner.PivotOffset = new Vector2(diameter * 0.5f, diameter * 0.5f);
            _spinner.Rotation = 0f;
            _spinner.SetSegments(_segments);
        }

        if (IsInstanceValid(_pointer) && _pointer.GetParent() is Control holder)
        {
            holder.CustomMinimumSize = new Vector2(diameter + 26f, diameter + 8f);
            _pointer.WheelDiameter = diameter;
        }

        QueueRedraw();
    }

    public void SetResultText(string text, Color? color = null)
    {
        if (!IsInstanceValid(_resultLabel))
        {
            return;
        }

        _resultLabel.Text = text;
        _resultLabel.AddThemeColorOverride("font_color", color ?? SpinUi.Cream);
        _resultLabel.Modulate = Colors.White;
        var tween = CreateTween();
        _resultLabel.Scale = new Vector2(0.9f, 0.9f);
        _resultLabel.PivotOffset = _resultLabel.Size * 0.5f;
        tween.TweenProperty(_resultLabel, "scale", Vector2.One, 0.25)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
    }

    public async Task<bool> SpinToAsync(string segmentKey, float duration, ulong visualSeed)
    {
        int index = _segments.FindIndex(s => s.Key == segmentKey);
        if (index < 0)
        {
            MainFile.Logger.Warn($"Prize wheel could not find segment '{segmentKey}'.");
            return false;
        }

        if (IsInstanceValid(_resultLabel))
        {
            _resultLabel.Text = "";
            _resultLabel.Modulate = Colors.White;
        }

        (float start, float end) = SegmentBounds(index);
        float segmentAngle = end - start;
        float centerAngle = start + segmentAngle * 0.5f;

        var rng = new Rng(visualSeed ^ (ulong)segmentKey.GetHashCode(StringComparison.Ordinal));
        float offset = rng.NextFloat(-0.30f, 0.30f) * segmentAngle;
        float landingAngle = centerAngle + offset;

        float current = _spinner.Rotation;
        float delta = NormalizePositive(-landingAngle - current);
        int turns = Mathf.Clamp(3 + Mathf.RoundToInt(Mathf.Max(0.4f, duration) * 1.4f), 3, 10);
        float target = current + Mathf.Tau * turns + delta;

        _spinner.BeginSpin(_segments);
        _pointer.BeginSpin();

        var done = new TaskCompletionSource<bool>();
        _spinDone = done;

        Tween tween = CreateTween();
        _spinTween = tween;
        tween.TweenProperty(_spinner, "rotation", target, Mathf.Max(0.35f, duration))
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);

        tween.Finished += () =>
        {
            if (_spinDone == done)
            {
                _spinDone = null;
                _spinTween = null;
                done.TrySetResult(true);
            }
        };

        bool completed = await done.Task;
        if (!completed)
        {
            return false;
        }

        _spinner.EndSpin();
        _pointer.EndSpin();
        return true;
    }

    public void StopSpin()
    {
        TaskCompletionSource<bool>? done = _spinDone;
        _spinDone = null;

        if (_spinTween != null && IsInstanceValid(_spinTween))
        {
            _spinTween.Kill();
        }

        _spinTween = null;
        done?.TrySetResult(false);

        if (IsInstanceValid(_spinner))
        {
            _spinner.Rotation = 0f;
            _spinner.EndSpin();
        }

        if (IsInstanceValid(_pointer))
        {
            _pointer.EndSpin();
        }

        if (IsInstanceValid(_resultLabel))
        {
            _resultLabel.Text = "";
            _resultLabel.Modulate = Colors.White;
        }
    }

    public (float Start, float End) SegmentBounds(int index)
    {
        int total = _segments.Sum(s => Mathf.Max(1, s.Weight));
        float cumulative = 0f;
        for (int i = 0; i < _segments.Count; i++)
        {
            float angle = Mathf.Tau * Mathf.Max(1, _segments[i].Weight) / total;
            if (i == index)
            {
                return (cumulative, cumulative + angle);
            }

            cumulative += angle;
        }

        return (0f, Mathf.Tau);
    }

    public int SegmentAtPointer(float rotation)
    {
        int total = _segments.Sum(s => Mathf.Max(1, s.Weight));
        float angleUnderPointer = NormalizePositive(-rotation);
        float cumulative = 0f;
        for (int i = 0; i < _segments.Count; i++)
        {
            float angle = Mathf.Tau * Mathf.Max(1, _segments[i].Weight) / total;
            if (angleUnderPointer >= cumulative && angleUnderPointer < cumulative + angle)
            {
                return i;
            }

            cumulative += angle;
        }

        return 0;
    }

    public static float NormalizePositive(float angle)
    {
        angle %= Mathf.Tau;
        return angle < 0f ? angle + Mathf.Tau : angle;
    }
}

internal sealed partial class PrizeWheelSpinner : Control
{
    private readonly List<PrizeWheel.Segment> _segments = new();
    private bool _spinning;
    private int _lastSegment = -1;
    private ulong _lastTickMs;

    public void SetSegments(List<PrizeWheel.Segment> segments)
    {
        _segments.Clear();
        _segments.AddRange(segments);
        QueueRedraw();
    }

    public void BeginSpin(List<PrizeWheel.Segment> segments)
    {
        SetSegments(segments);
        _spinning = true;
        _lastSegment = -1;
        _lastTickMs = 0;
    }

    public void EndSpin()
    {
        _spinning = false;
    }

    private int SegmentUnderPointer(float rotation)
    {
        int total = _segments.Sum(s => Mathf.Max(1, s.Weight));
        float angleUnderPointer = PrizeWheel.NormalizePositive(-rotation);
        float cumulative = 0f;
        for (int i = 0; i < _segments.Count; i++)
        {
            float angle = Mathf.Tau * Mathf.Max(1, _segments[i].Weight) / total;
            if (angleUnderPointer >= cumulative && angleUnderPointer < cumulative + angle)
            {
                return i;
            }

            cumulative += angle;
        }

        return 0;
    }

    public override void _Process(double delta)
    {
        if (!_spinning || _segments.Count == 0)
        {
            return;
        }

        int segment = SegmentUnderPointer(Rotation);
        if (segment == _lastSegment)
        {
            return;
        }

        _lastSegment = segment;
        ulong now = Time.GetTicksMsec();
        if (now - _lastTickMs < 38)
        {
            return;
        }

        _lastTickMs = now;
        try
        {
            NDebugAudioManager.Instance?.Play("map_split_tick.mp3", 0.07f, PitchVariance.Small);
        }
        catch
        {
            // Audio is cosmetic; never let it break the spin.
        }
    }

    public override void _Draw()
    {
        if (_segments.Count == 0)
        {
            return;
        }

        Vector2 center = Size * 0.5f;
        float radius = Mathf.Min(Size.X, Size.Y) * 0.5f - 8f;
        if (radius <= 0f)
        {
            return;
        }

        DrawCircle(center + new Vector2(5, 7), radius + 5f, new Color(0, 0, 0, 0.38f));

        int total = _segments.Sum(s => Mathf.Max(1, s.Weight));
        float cumulative = 0f;
        for (int i = 0; i < _segments.Count; i++)
        {
            PrizeWheel.Segment segment = _segments[i];
            float angle = Mathf.Tau * Mathf.Max(1, segment.Weight) / total;
            DrawSlice(center, radius, cumulative, cumulative + angle, segment.Color);
            cumulative += angle;
        }

        // Thick teal rim matching the game's native buttons, wrapped in a dark border.
        float rimWidth = Mathf.Max(18f, radius * 0.078f);
        DrawArc(center, radius - rimWidth * 0.5f, 0f, Mathf.Tau, 180,
            new Color(0.05f, 0.10f, 0.11f, 1f), rimWidth + 7f, true);
        DrawArc(center, radius - rimWidth * 0.5f, 0f, Mathf.Tau, 180,
            new Color("#3F8B91"), rimWidth, true);
        DrawArc(center, radius - rimWidth + 1f, 0f, Mathf.Tau, 180,
            new Color(0.63f, 0.93f, 0.89f, 0.35f), 2f, true);

        DrawHub(center, radius);
        DrawLabels(center, radius, total);
    }

    private void DrawHub(Vector2 center, float radius)
    {
        float hubRadius = radius * 0.155f;

        DrawCircle(center + new Vector2(0f, 2f), hubRadius + 4f, new Color(0f, 0f, 0f, 0.35f));
        DrawCircle(center, hubRadius + 3.5f, new Color(0.05f, 0.16f, 0.17f, 1f));
        DrawCircle(center, hubRadius, new Color("#2C7A7B"));
        DrawCircle(center, hubRadius * 0.86f, new Color("#358F90"));
        DrawArc(center, hubRadius - 1f, 0f, Mathf.Tau, 48,
            new Color(0.62f, 0.92f, 0.88f, 0.55f), 1.5f, true);

        DrawLoopArrows(center, hubRadius);
    }

    private void DrawLoopArrows(Vector2 center, float hubRadius)
    {
        Color gold = SpinUi.Gold;
        float arrowRadius = hubRadius * 0.55f;
        float thickness = Mathf.Max(3f, hubRadius * 0.15f);

        DrawArc(center, arrowRadius, Mathf.DegToRad(-160f), Mathf.DegToRad(-34f), 24,
            gold, thickness, true);
        DrawArc(center, arrowRadius, Mathf.DegToRad(20f), Mathf.DegToRad(146f), 24,
            gold, thickness, true);

        DrawArrowHead(center, arrowRadius, Mathf.DegToRad(-34f), gold, hubRadius);
        DrawArrowHead(center, arrowRadius, Mathf.DegToRad(146f), gold, hubRadius);
    }

    private void DrawArrowHead(Vector2 center, float arrowRadius, float angle, Color color, float hubRadius)
    {
        Vector2 tangent = new(-Mathf.Sin(angle), Mathf.Cos(angle));
        Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * arrowRadius;
        Vector2 normal = new(-tangent.Y, tangent.X);
        float length = hubRadius * 0.34f;
        float halfWidth = hubRadius * 0.18f;

        DrawColoredPolygon(
            new[]
            {
                point + tangent * length,
                point - normal * halfWidth,
                point + normal * halfWidth
            },
            color);
    }

    private void DrawSlice(Vector2 center, float radius, float start, float end, Color color)
    {
        // A single segment spans a full turn, which makes the first and last perimeter points
        // coincide and breaks polygon triangulation (the wheel rendered black). Fill it directly.
        if (end - start >= Mathf.Tau - 0.0001f)
        {
            DrawCircle(center, radius, color);
            return;
        }

        int steps = Mathf.Max(2, Mathf.CeilToInt((end - start) / (Mathf.Tau / 96f)));
        var points = new Vector2[steps + 2];
        points[0] = center;
        for (int i = 0; i <= steps; i++)
        {
            float a = Mathf.Lerp(start, end, i / (float)steps);
            points[i + 1] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        DrawColoredPolygon(points, color);

        Vector2 startPoint = center + new Vector2(Mathf.Cos(start), Mathf.Sin(start)) * radius;
        DrawLine(center, startPoint, new Color(0.08f, 0.06f, 0.03f, 0.55f), 1.2f, true);
    }

    private void DrawLabels(Vector2 center, float radius, int total)
    {
        Font font = SpinUi.WheelFont;
        float maxWidth = radius * 0.66f;
        float preferredSize = Mathf.Clamp(radius * 0.10f, 14f, 44f);
        float startX = radius * 0.235f;
        float cumulative = 0f;

        for (int i = 0; i < _segments.Count; i++)
        {
            PrizeWheel.Segment segment = _segments[i];
            float angle = Mathf.Tau * Mathf.Max(1, segment.Weight) / total;
            float mid = _segments.Count == 1 ? 0f : cumulative + angle * 0.5f;
            cumulative += angle;

            if (angle < 0.035f)
            {
                continue;
            }

            string text = segment.Label;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            // Shrink to fit instead of truncating with "...", so full names stay readable.
            float fontSize = FitFontSize(font, text, maxWidth, preferredSize);

            DrawSetTransform(center, mid, Vector2.One);
            Color color = SpinUi.TextOn(segment.Color);
            DrawString(
                font,
                new Vector2(startX, fontSize * 0.35f),
                text,
                HorizontalAlignment.Left,
                maxWidth,
                (int)fontSize,
                color);
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        }
    }

    private static float FitFontSize(Font font, string text, float maxWidth, float preferredSize)
    {
        float size = preferredSize;
        while (size > 9f
               && font.GetStringSize(text, HorizontalAlignment.Left, -1, (int)size).X > maxWidth)
        {
            size -= 0.5f;
        }

        return size;
    }
}

internal sealed partial class PrizeWheelPointer : Control
{
    public float WheelDiameter { get; set; } = 400f;

    public override void _Draw()
    {
        float x = WheelDiameter + 3f;
        float y = WheelDiameter * 0.5f;
        var points = new[]
        {
            new Vector2(x + 22f, y - 18f),
            new Vector2(x + 22f, y + 18f),
            new Vector2(x - 6f, y)
        };
        DrawColoredPolygon(points, SpinUi.Gold);
        DrawPolyline(
            new[] { points[0], points[1], points[2], points[0] },
            new Color(0.12f, 0.08f, 0.03f, 0.95f),
            3f,
            true);
    }

    public void BeginSpin()
    {
    }

    public void EndSpin()
    {
    }
}
