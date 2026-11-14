using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace SprocketUiKit;

/// <summary>
/// Circular progress (0.1.8): a procedural ring sprite shown twice, as a muted track and as a
/// radially filled image (GPU <see cref="Image.Type.Filled"/>, no CPU raster per value). The value
/// is clamped to 0..1 (NaN/infinite = 0); the ring fills clockwise from 12 o'clock. An optional
/// centre text (TextStyle.Value or smaller) is only set when it changes; the fillAmount is only
/// updated for a change of at least 1/720. Works in a panel and in <see cref="HudPanel.Body"/>.
/// </summary>
public sealed class RadialProgress : Element
{
    private readonly Func<float> value;
    private readonly Func<string?>? centre;
    private readonly RadialPoll poll = new();
    private float thickness;
    private bool thicknessSet;
    private Color colour = Draw.C(Tok.Accent);
    private Func<Color>? colourGetter;
    private Rgba trackColour = Tok.RadialTrack;
    private Image? track, fill;
    private TextMeshProUGUI? centreText;
    private RingKey spriteKey;
    private bool hasSprite;

    internal RadialProgress(Func<float> value, float size, Func<string?>? centre)
    {
        this.value = value; this.centre = centre;
        float s = float.IsFinite(size) ? MathF.Max(1, size) : 48;
        FixedW = s; FixedH = s;
        thickness = RadialMath.DefaultThickness(s);
    }

    /// <summary>Ring thickness in reference px (default about 12% of the diameter, at least 3 px).</summary>
    public RadialProgress Thickness(float px)
    {
        thickness = float.IsFinite(px) ? MathF.Max(.5f, px) : RadialMath.MinThickness;
        thicknessSet = true;
        return this;
    }
    /// <summary>Fill colour (default <see cref="Tok.Accent"/>).</summary>
    public RadialProgress Colour(Color colour)
    {
        this.colour = colour; colourGetter = null;
        if (fill != null) fill.color = colour;
        return this;
    }
    /// <summary>Fill colour from a getter, polled every frame.</summary>
    public RadialProgress Colour(Func<Color> colour) { colourGetter = colour; return this; }
    /// <summary>Background ring colour (default the muted <see cref="Tok.RadialTrack"/>).</summary>
    public RadialProgress Track(Color colour)
    {
        trackColour = Draw.R(colour);
        if (track != null) track.color = colour;
        return this;
    }

    protected override void Build()
    {
        track = Draw.Child(Rt!, "Track", null, trackColour);
        fill = Draw.Child(Rt!, "Fill", null, Draw.R(colour));
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Top;
        fill.fillClockwise = true;
        fill.fillAmount = 0;
        centreText = Draw.Text(Draw.Node("Centre", Rt!), TextStyle.Value);
        centreText.alignment = TextAlignmentOptions.Midline;
        centreText.textWrappingMode = TextWrappingModes.NoWrap;
        centreText.overflowMode = TextOverflowModes.Overflow;
        Draw.SetActive(centreText, false);
        hasSprite = false;
        UpdateSprite(FixedW >= 0 ? FixedW : 48);
    }
    internal override void Unbuild()
    {
        track = fill = null; centreText = null; hasSprite = false;
        base.Unbuild();
    }

    private void UpdateSprite(float diameter)
    {
        if (track == null || fill == null || diameter <= 0) return;
        float t = thicknessSet ? thickness : RadialMath.DefaultThickness(diameter);
        var key = RadialMath.SpriteKey(diameter, t, Kit.CurrentScale(Panel));
        if (hasSprite && key.Equals(spriteKey)) return;
        spriteKey = key; hasSprite = true;
        var sprite = Assets.Ring(key);
        if (track.sprite != sprite) track.sprite = sprite;
        if (fill.sprite != sprite) fill.sprite = sprite;
        float font = RadialMath.CentreFontSize(diameter);
        if (centreText != null && MathF.Abs(centreText.fontSize - font) > .01f) centreText.fontSize = font;
    }

    internal override float IntrinsicWidth() => FixedW >= 0 ? FixedW : 48;
    internal override float MeasureHeight(float width) => FixedH >= 0 ? FixedH : 48;

    internal override void ArrangeCore(RectF r, Vec origin, RectF clip)
    {
        base.ArrangeCore(r, origin, clip);
        float d = MathF.Max(0, MathF.Min(r.W, r.H));
        float ox = (r.W - d) / 2, oy = (r.H - d) / 2;
        if (track != null) Draw.Place(track.rectTransform, ox, oy, d, d);
        if (fill != null) Draw.Place(fill.rectTransform, ox, oy, d, d);
        if (centreText != null)
        {
            float pad = d * .18f;
            Draw.Place(centreText.rectTransform, ox + pad, oy + pad, MathF.Max(0, d - 2 * pad), MathF.Max(0, d - 2 * pad));
        }
        UpdateSprite(d);
    }

    internal override void Refresh(float now)
    {
        if (track == null || fill == null) return;
        float raw = Guard.Get(Site(0, ".Value"), value, 0f);
        string? text = centre != null ? Guard.Get(Site(1, ".Centre"), centre, (string?)null) : null;
        var changed = poll.Poll(raw, text);
        UpdateSprite(MathF.Min(Rect.W, Rect.H));
        if ((changed & RadialChange.Value) != 0) fill.fillAmount = poll.Value;
        if ((changed & RadialChange.Text) != 0)
        {
            Draw.SetText(centreText, poll.Text);
            Draw.SetActive(centreText, !string.IsNullOrEmpty(poll.Text));
        }
        Color want = colourGetter != null ? Guard.Get(Site(2, ".Colour"), colourGetter, colour) : colour;
        if (fill.color != want) fill.color = want;
        Draw.SetColor(track, trackColour);
    }
}
