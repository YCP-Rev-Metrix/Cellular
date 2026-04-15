using Cellular.Cloud_API.Models;

namespace Cellular.Views;

public class CiclopesLinePlotDrawable : IDrawable
{
    private readonly IReadOnlyList<CiclopesBallPoint> _points;

    public CiclopesLinePlotDrawable(IReadOnlyList<CiclopesBallPoint> points)
    {
        _points = points;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.SaveState();
        canvas.Antialias = true;

        if (_points.Count < 2)
        {
            canvas.RestoreState();
            return;
        }

        const float padLeft = 8f;
        const float padRight = 8f;
        const float padBottom = 6f;
        const float padTop = 4f;

        var plotRect = new RectF(
            dirtyRect.Left + padLeft,
            dirtyRect.Top + padTop,
            dirtyRect.Width - padLeft - padRight,
            dirtyRect.Height - padTop - padBottom);

        var minX = _points.Min(p => p.X);
        var maxX = _points.Max(p => p.X);
        var minY = _points.Min(p => p.Y);
        var maxY = _points.Max(p => p.Y);

        var rangeX = maxX - minX;
        var rangeY = maxY - minY;
        if (rangeX < 0.001) rangeX = 1;
        if (rangeY < 0.001) rangeY = 1;

        // Draw axes
        canvas.StrokeColor = Color.FromArgb("#d0d0d0");
        canvas.StrokeSize = 1f;
        canvas.DrawLine(plotRect.Left, plotRect.Bottom, plotRect.Right, plotRect.Bottom);
        canvas.DrawLine(plotRect.Left, plotRect.Top, plotRect.Left, plotRect.Bottom);

        // Fill area under the line
        var fillPath = new PathF();
        var firstNormY = (float)((_points[0].Y - minY) / rangeY);
        var firstScreenX = plotRect.Left + (float)((_points[0].X - minX) / rangeX) * plotRect.Width;
        var firstScreenY = plotRect.Bottom - firstNormY * plotRect.Height;
        fillPath.MoveTo(firstScreenX, plotRect.Bottom);
        fillPath.LineTo(firstScreenX, firstScreenY);

        for (var i = 1; i < _points.Count; i++)
        {
            var normX = (float)((_points[i].X - minX) / rangeX);
            var normY = (float)((_points[i].Y - minY) / rangeY);
            var screenX = plotRect.Left + normX * plotRect.Width;
            var screenY = plotRect.Bottom - normY * plotRect.Height;
            fillPath.LineTo(screenX, screenY);
        }

        var lastScreenX = plotRect.Left + (float)((_points[^1].X - minX) / rangeX) * plotRect.Width;
        fillPath.LineTo(lastScreenX, plotRect.Bottom);
        fillPath.Close();

        canvas.FillColor = Color.FromArgb("#206b5b95");
        canvas.FillPath(fillPath);

        // Draw the line
        canvas.StrokeColor = Color.FromArgb("#355070");
        canvas.StrokeSize = 2f;

        for (var i = 1; i < _points.Count; i++)
        {
            var prevNormX = (float)((_points[i - 1].X - minX) / rangeX);
            var prevNormY = (float)((_points[i - 1].Y - minY) / rangeY);
            var currNormX = (float)((_points[i].X - minX) / rangeX);
            var currNormY = (float)((_points[i].Y - minY) / rangeY);

            canvas.DrawLine(
                plotRect.Left + prevNormX * plotRect.Width,
                plotRect.Bottom - prevNormY * plotRect.Height,
                plotRect.Left + currNormX * plotRect.Width,
                plotRect.Bottom - currNormY * plotRect.Height);
        }

        // Draw points
        canvas.FillColor = Color.FromArgb("#355070");
        foreach (var pt in _points)
        {
            var normX = (float)((pt.X - minX) / rangeX);
            var normY = (float)((pt.Y - minY) / rangeY);
            var screenX = plotRect.Left + normX * plotRect.Width;
            var screenY = plotRect.Bottom - normY * plotRect.Height;
            canvas.FillCircle(screenX, screenY, 2.5f);
        }

        canvas.RestoreState();
    }
}
