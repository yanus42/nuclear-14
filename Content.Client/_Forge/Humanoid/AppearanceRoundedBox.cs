using Robust.Client.Graphics;

namespace Content.Client._Forge.Humanoid;

// Same rounded geometry as StyleNewVegas.RoundedButtonBox; classic theme supplies tint.
public sealed class AppearanceRoundedBox : StyleBox
{
    public AppearanceRoundedBox()
    {
        SetContentMarginOverride(Margin.Horizontal, 8);
        SetContentMarginOverride(Margin.Vertical, 6);
    }
        protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale)
        {
            var radius = MathF.Min(10 * uiScale, MathF.Min(box.Width, box.Height) / 2);
            Fill(handle, box, radius, new Color(0.7f, 0.7f, 0.7f, 1f));
            var inner = new UIBox2(box.Left + uiScale, box.Top + uiScale,
                box.Right - uiScale, box.Bottom - uiScale);
            if (inner.Width > 0 && inner.Height > 0)
                Fill(handle, inner, MathF.Max(0, radius - uiScale), Color.White);
        }

        private static void Fill(DrawingHandleScreen handle, UIBox2 box, float radius, Color color)
        {
            if (radius <= 0)
            {
                handle.DrawRect(box, color);
                return;
            }

            handle.DrawRect(new UIBox2(box.Left, box.Top + radius, box.Right, box.Bottom - radius), color);
            // Each one-pixel strip follows the circular edge. Unlike separate corner circles,
            // these use DrawRect throughout, so the fill and corners have identical color handling.
            for (var y = 0f; y < radius; y += 1f)
            {
                var end = MathF.Min(y + 1f, radius);
                var distance = radius - (y + end) / 2f;
                var inset = radius - MathF.Sqrt(MathF.Max(0, radius * radius - distance * distance));
                handle.DrawRect(new UIBox2(box.Left + inset, box.Top + y,
                    box.Right - inset, box.Top + end), color);
                handle.DrawRect(new UIBox2(box.Left + inset, box.Bottom - end,
                    box.Right - inset, box.Bottom - y), color);
            }
        }
    }
