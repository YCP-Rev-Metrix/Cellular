using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Cellular
{
    public partial class SensorGraphPage : ContentPage
    {
        private readonly List<SensorDataPoint> _accelerometerData;
        private readonly List<SensorDataPoint> _gyroscopeData;
        private readonly List<SensorDataPoint> _magnetometerData;
        private readonly List<SensorDataPoint> _lightSensorData;
        private readonly Action? _onClear;

        public SensorGraphPage(
            List<SensorDataPoint> accelerometerData,
            List<SensorDataPoint> gyroscopeData,
            List<SensorDataPoint> magnetometerData,
            List<SensorDataPoint> lightSensorData,
            Action? onClear = null)
        {
            InitializeComponent();

            _accelerometerData = accelerometerData;
            _gyroscopeData = gyroscopeData;
            _magnetometerData = magnetometerData;
            _lightSensorData = lightSensorData;
            _onClear = onClear;

            SetupCharts();
        }

        private void SetupCharts()
        {
            AccelerometerChart.Drawable = new SensorChartDrawable(_accelerometerData, "Accelerometer");
            GyroscopeChart.Drawable = new SensorChartDrawable(_gyroscopeData, "Gyroscope");
            MagnetometerChart.Drawable = new SensorChartDrawable(_magnetometerData, "Magnetometer");
            LightSensorChart.Drawable = new LightSensorChartDrawable(_lightSensorData);
        }

        private void OnClearDataClicked(object sender, EventArgs e)
        {
            AccelerometerChart.Drawable = null;
            GyroscopeChart.Drawable = null;
            MagnetometerChart.Drawable = null;
            LightSensorChart.Drawable = null;

            // Clear the live data collections on the SmartDot page
            _onClear?.Invoke();

            AccelerometerChart.Invalidate();
            GyroscopeChart.Invalidate();
            MagnetometerChart.Invalidate();
            LightSensorChart.Invalidate();
        }
    }

    public class SensorChartDrawable : IDrawable
    {
        private readonly List<SensorDataPoint> _data;
        private readonly string _name;

        // Ticks on each axis
        private const int NumTicks = 5;
        // Asymmetric padding: left needs room for Y labels, bottom for X labels
        private const float PadLeft = 55f;
        private const float PadRight = 10f;
        private const float PadTop = 10f;
        private const float PadBottom = 32f;

        public SensorChartDrawable(List<SensorDataPoint> data, string name)
        {
            _data = data;
            _name = name;
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (_data == null || _data.Count == 0)
            {
                canvas.FontColor = Colors.Gray;
                canvas.FontSize = 14;
                canvas.DrawString("No data available", dirtyRect, HorizontalAlignment.Center, VerticalAlignment.Center);
                return;
            }

            float chartX = PadLeft;
            float chartY = PadTop;
            float chartWidth = dirtyRect.Width - PadLeft - PadRight;
            float chartHeight = dirtyRect.Height - PadTop - PadBottom;

            // Time range
            var startTime = _data[0].Timestamp;
            var endTime = _data[_data.Count - 1].Timestamp;
            double timeRange = (endTime - startTime).TotalSeconds;
            if (timeRange < 0.1) timeRange = 0.1;

            // Value range across X, Y, Z
            double minValue = Math.Min(Math.Min(_data.Min(d => d.X), _data.Min(d => d.Y)), _data.Min(d => d.Z));
            double maxValue = Math.Max(Math.Max(_data.Max(d => d.X), _data.Max(d => d.Y)), _data.Max(d => d.Z));
            double valueRange = maxValue - minValue;
            if (valueRange < 0.1) valueRange = 0.1;
            minValue -= valueRange * 0.08;
            maxValue += valueRange * 0.08;
            valueRange = maxValue - minValue;

            // Grid lines + Y ticks
            string yFmt = Math.Abs(maxValue) > 100 || Math.Abs(minValue) > 100 ? "F0" : "F1";
            canvas.FontSize = 9;
            for (int i = 0; i <= NumTicks; i++)
            {
                double tickVal = minValue + valueRange * i / NumTicks;
                float tickY = chartY + chartHeight - (float)((tickVal - minValue) / valueRange * chartHeight);

                // Grid line
                canvas.StrokeColor = Color.FromRgba(200, 200, 200, 180);
                canvas.StrokeSize = 0.5f;
                canvas.DrawLine(chartX, tickY, chartX + chartWidth, tickY);

                // Tick mark
                canvas.StrokeColor = Colors.Black;
                canvas.StrokeSize = 1;
                canvas.DrawLine(chartX - 4, tickY, chartX, tickY);

                // Label
                canvas.FontColor = Colors.Black;
                canvas.DrawString(tickVal.ToString(yFmt), new RectF(0, tickY - 8, chartX - 6, 16),
                    HorizontalAlignment.Right, VerticalAlignment.Center);
            }

            // X ticks
            for (int i = 0; i <= NumTicks; i++)
            {
                double tickTime = timeRange * i / NumTicks;
                float tickX = chartX + (float)(tickTime / timeRange * chartWidth);

                // Tick mark
                canvas.StrokeColor = Colors.Black;
                canvas.StrokeSize = 1;
                canvas.DrawLine(tickX, chartY + chartHeight, tickX, chartY + chartHeight + 4);

                // Label
                canvas.FontColor = Colors.Black;
                canvas.FontSize = 9;
                canvas.DrawString(tickTime.ToString("F1"), new RectF(tickX - 18, chartY + chartHeight + 5, 36, 14),
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }

            // "Time (s)" label
            canvas.FontSize = 9;
            canvas.FontColor = Colors.DarkGray;
            canvas.DrawString("Time (s)", new RectF(chartX + chartWidth / 2 - 25, dirtyRect.Height - 12, 50, 12),
                HorizontalAlignment.Center, VerticalAlignment.Center);

            // Axes
            canvas.StrokeColor = Colors.Black;
            canvas.StrokeSize = 1.5f;
            canvas.DrawLine(chartX, chartY + chartHeight, chartX + chartWidth, chartY + chartHeight);
            canvas.DrawLine(chartX, chartY, chartX, chartY + chartHeight);

            // Series
            if (_data.Count > 1)
            {
                canvas.StrokeColor = Colors.Red;
                canvas.StrokeSize = 1.5f;
                DrawLineSeries(canvas, _data, startTime, timeRange, minValue, valueRange, chartX, chartY, chartWidth, chartHeight, d => d.X);

                canvas.StrokeColor = Colors.Green;
                canvas.StrokeSize = 1.5f;
                DrawLineSeries(canvas, _data, startTime, timeRange, minValue, valueRange, chartX, chartY, chartWidth, chartHeight, d => d.Y);

                canvas.StrokeColor = Colors.Blue;
                canvas.StrokeSize = 1.5f;
                DrawLineSeries(canvas, _data, startTime, timeRange, minValue, valueRange, chartX, chartY, chartWidth, chartHeight, d => d.Z);
            }
        }

        private static void DrawLineSeries(ICanvas canvas, List<SensorDataPoint> data, DateTime startTime, double timeRange, double minValue, double valueRange, float chartX, float chartY, float chartWidth, float chartHeight, Func<SensorDataPoint, double> valueSelector)
        {
            var path = new PathF();
            bool first = true;
            foreach (var point in data)
            {
                float x = chartX + (float)((point.Timestamp - startTime).TotalSeconds / timeRange * chartWidth);
                float y = chartY + chartHeight - (float)((valueSelector(point) - minValue) / valueRange * chartHeight);
                if (first) { path.MoveTo(x, y); first = false; }
                else path.LineTo(x, y);
            }
            canvas.DrawPath(path);
        }
    }

    public class LightSensorChartDrawable : IDrawable
    {
        private readonly List<SensorDataPoint> _data;

        private const int NumTicks = 5;
        private const float PadLeft = 55f;
        private const float PadRight = 10f;
        private const float PadTop = 10f;
        private const float PadBottom = 32f;

        public LightSensorChartDrawable(List<SensorDataPoint> data)
        {
            _data = data;
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (_data == null || _data.Count == 0)
            {
                canvas.FontColor = Colors.Gray;
                canvas.FontSize = 14;
                canvas.DrawString("No data available", dirtyRect, HorizontalAlignment.Center, VerticalAlignment.Center);
                return;
            }

            float chartX = PadLeft;
            float chartY = PadTop;
            float chartWidth = dirtyRect.Width - PadLeft - PadRight;
            float chartHeight = dirtyRect.Height - PadTop - PadBottom;

            var startTime = _data[0].Timestamp;
            double timeRange = (_data[_data.Count - 1].Timestamp - startTime).TotalSeconds;
            if (timeRange < 0.1) timeRange = 0.1;

            double minValue = (double)_data.Min(d => d.X);
            double maxValue = (double)_data.Max(d => d.X);
            double valueRange = maxValue - minValue;
            if (valueRange < 0.1) valueRange = 0.1;
            minValue -= valueRange * 0.08;
            maxValue += valueRange * 0.08;
            valueRange = maxValue - minValue;

            string yFmt = maxValue > 999 ? "F0" : "F1";

            // Grid lines + Y ticks
            canvas.FontSize = 9;
            for (int i = 0; i <= NumTicks; i++)
            {
                double tickVal = minValue + valueRange * i / NumTicks;
                float tickY = chartY + chartHeight - (float)((tickVal - minValue) / valueRange * chartHeight);

                canvas.StrokeColor = Color.FromRgba(200, 200, 200, 180);
                canvas.StrokeSize = 0.5f;
                canvas.DrawLine(chartX, tickY, chartX + chartWidth, tickY);

                canvas.StrokeColor = Colors.Black;
                canvas.StrokeSize = 1;
                canvas.DrawLine(chartX - 4, tickY, chartX, tickY);

                canvas.FontColor = Colors.Black;
                canvas.DrawString(tickVal.ToString(yFmt), new RectF(0, tickY - 8, chartX - 6, 16),
                    HorizontalAlignment.Right, VerticalAlignment.Center);
            }

            // X ticks
            for (int i = 0; i <= NumTicks; i++)
            {
                double tickTime = timeRange * i / NumTicks;
                float tickX = chartX + (float)(tickTime / timeRange * chartWidth);

                canvas.StrokeColor = Colors.Black;
                canvas.StrokeSize = 1;
                canvas.DrawLine(tickX, chartY + chartHeight, tickX, chartY + chartHeight + 4);

                canvas.FontColor = Colors.Black;
                canvas.FontSize = 9;
                canvas.DrawString(tickTime.ToString("F1"), new RectF(tickX - 18, chartY + chartHeight + 5, 36, 14),
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }

            canvas.FontSize = 9;
            canvas.FontColor = Colors.DarkGray;
            canvas.DrawString("Time (s)", new RectF(chartX + chartWidth / 2 - 25, dirtyRect.Height - 12, 50, 12),
                HorizontalAlignment.Center, VerticalAlignment.Center);

            // Axes
            canvas.StrokeColor = Colors.Black;
            canvas.StrokeSize = 1.5f;
            canvas.DrawLine(chartX, chartY + chartHeight, chartX + chartWidth, chartY + chartHeight);
            canvas.DrawLine(chartX, chartY, chartX, chartY + chartHeight);

            // Orange series
            if (_data.Count > 1)
            {
                canvas.StrokeColor = Colors.Orange;
                canvas.StrokeSize = 1.5f;
                var path = new PathF();
                bool first = true;
                foreach (var point in _data)
                {
                    float x = chartX + (float)((point.Timestamp - startTime).TotalSeconds / timeRange * chartWidth);
                    float y = chartY + chartHeight - (float)((point.X - minValue) / valueRange * chartHeight);
                    if (first) { path.MoveTo(x, y); first = false; }
                    else path.LineTo(x, y);
                }
                canvas.DrawPath(path);
            }
        }
    }
}
