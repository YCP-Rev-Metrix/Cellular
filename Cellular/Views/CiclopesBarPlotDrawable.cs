namespace Cellular.Views;

public class CiclopesBarPlotDrawable : IDrawable
{
    private readonly float[] _values;
    private readonly string[] _labels;
    private readonly Color _barColor;
    private readonly Color _barHighlight;

    public CiclopesBarPlotDrawable(float[] values, string[] labels, Color barColor, Color barHighlight)
    {
        _values = values;
        _labels = labels;
        _barColor = barColor;
        _barHighlight = barHighlight;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.SaveState();
        canvas.Antialias = true;

        if (_values.Length == 0)
        {
            canvas.RestoreState();
            return;
        }

        const float padLeft = 6f;
        const float padRight = 6f;
        const float padBottom = 16f;
        const float padTop = 4f;

        var plotRect = new RectF(
            dirtyRect.Left + padLeft,
            dirtyRect.Top + padTop,
            dirtyRect.Width - padLeft - padRight,
            dirtyRect.Height - padTop - padBottom);

        // Determine value range — handle negative values
        var maxVal = _values.Max();
        var minVal = _values.Min();
        if (minVal > 0) minVal = 0;
        if (maxVal < 0) maxVal = 0;
        var range = maxVal - minVal;
        if (range < 0.001f) range = 1f;

        var zeroY = plotRect.Top + (maxVal / range) * plotRect.Height;

        // Draw zero line
        canvas.StrokeColor = Color.FromArgb("#d0d0d0");
        canvas.StrokeSize = 1f;
        canvas.DrawLine(plotRect.Left, zeroY, plotRect.Right, zeroY);

        // Draw bars
        var barCount = _values.Length;
        var barSpacing = 4f;
        var totalSpacing = barSpacing * (barCount - 1);
        var barWidth = (plotRect.Width - totalSpacing) / barCount;
        if (barWidth > 28f) barWidth = 28f;

        var totalBarsWidth = barWidth * barCount + totalSpacing;
        var startX = plotRect.Left + (plotRect.Width - totalBarsWidth) / 2f;

        canvas.FontSize = 8f;
        canvas.FontColor = Color.FromArgb("#888888");

        for (var i = 0; i < barCount; i++)
        {
            var x = startX + i * (barWidth + barSpacing);
            var val = _values[i];
            var barHeight = Math.Abs(val / range) * plotRect.Height;

            float barTop;
            if (val >= 0)
            {
                barTop = zeroY - barHeight;
            }
            else
            {
                barTop = zeroY;
            }

            // Bar fill — use highlight for max value
            var isMax = Math.Abs(val - _values.Max()) < 0.001f;
            canvas.FillColor = isMax ? _barHighlight : _barColor;
            canvas.FillRoundedRectangle(x, barTop, barWidth, barHeight, 3f);

            // Label
            if (i < _labels.Length)
            {
                canvas.DrawString(_labels[i],
                    x, plotRect.Bottom + 2f, barWidth, 14f,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }
        }

        canvas.RestoreState();
    }
}
