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

        public SensorGraphPage(
            List<SensorDataPoint> accelerometerData,
            List<SensorDataPoint> gyroscopeData,
            List<SensorDataPoint> magnetometerData,
            List<SensorDataPoint> lightSensorData)
        {
            InitializeComponent();
            
            _accelerometerData = accelerometerData;
            _gyroscopeData = gyroscopeData;
            _magnetometerData = magnetometerData;
            _lightSensorData = lightSensorData;
            
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
        }
    }

    public class SensorChartDrawable : IDrawable
    {
        private readonly List<SensorDataPoint> _data;
        private readonly string _name;

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

            var padding = 40f;
            var chartWidth = dirtyRect.Width - (padding * 2);
            var chartHeight = dirtyRect.Height - (padding * 2);
            var chartX = padding;
            var chartY = padding;

            // Calculate time range
            var startTime = _data[0].Timestamp;
            var endTime = _data[_data.Count - 1].Timestamp;
            var timeRange = (endTime - startTime).TotalSeconds;
            if (timeRange < 0.1) timeRange = 0.1; // Minimum range

            // Calculate value ranges for X, Y, Z
            var xValues = _data.Select(d => d.X).ToList();
            var yValues = _data.Select(d => d.Y).ToList();
            var zValues = _data.Select(d => d.Z).ToList();
            
            var minX = xValues.Min();
            var maxX = xValues.Max();
            var minY = yValues.Min();
            var maxY = yValues.Max();
            var minZ = zValues.Min();
            var maxZ = zValues.Max();
            
            var minValue = Math.Min(Math.Min(minX, minY), minZ);
            var maxValue = Math.Max(Math.Max(maxX, maxY), maxZ);
            
            // Add padding to value range
            var valueRange = maxValue - minValue;
            if (valueRange < 0.1) valueRange = 0.1;
            minValue -= valueRange * 0.1f;
            maxValue += valueRange * 0.1f;
            valueRange = maxValue - minValue;

            // Draw axes
            canvas.StrokeColor = Colors.Black;
            canvas.StrokeSize = 1;
            canvas.DrawLine(chartX, chartY + chartHeight, chartX + chartWidth, chartY + chartHeight); // X axis
            canvas.DrawLine(chartX, chartY, chartX, chartY + chartHeight); // Y axis

            // Draw X axis label
            canvas.FontColor = Colors.Black;
            canvas.FontSize = 10;
            var xLabelRect = new RectF(chartX + chartWidth / 2 - 30, chartY + chartHeight + 25, 60, 15);
            canvas.DrawString("Time (s)", xLabelRect, HorizontalAlignment.Center, VerticalAlignment.Top);

            // Draw Y axis label
            canvas.SaveState();
            canvas.Rotate(-90, chartX - 20, chartY + chartHeight / 2);
            var yLabelRect = new RectF(chartX - 20 - 30, chartY + chartHeight / 2 - 10, 60, 20);
            canvas.DrawString("Value", yLabelRect, HorizontalAlignment.Center, VerticalAlignment.Center);
            canvas.RestoreState();

            // Draw X series (Red)
            if (_data.Count > 1)
            {
                canvas.StrokeColor = Colors.Red;
                canvas.StrokeSize = 2;
                DrawLineSeries(canvas, _data, startTime, timeRange, minValue, valueRange, chartX, chartY, chartWidth, chartHeight, d => d.X);
            }

            // Draw Y series (Green)
            if (_data.Count > 1)
            {
                canvas.StrokeColor = Colors.Green;
                canvas.StrokeSize = 2;
                DrawLineSeries(canvas, _data, startTime, timeRange, minValue, valueRange, chartX, chartY, chartWidth, chartHeight, d => d.Y);
            }

            // Draw Z series (Blue)
            if (_data.Count > 1)
            {
                canvas.StrokeColor = Colors.Blue;
                canvas.StrokeSize = 2;
                DrawLineSeries(canvas, _data, startTime, timeRange, minValue, valueRange, chartX, chartY, chartWidth, chartHeight, d => d.Z);
            }
        }

        private void DrawLineSeries(ICanvas canvas, List<SensorDataPoint> data, DateTime startTime, double timeRange, double minValue, double valueRange, float chartX, float chartY, float chartWidth, float chartHeight, Func<SensorDataPoint, double> valueSelector)
        {
            var path = new PathF();
            bool first = true;

            for (int i = 0; i < data.Count; i++)
            {
                var point = data[i];
                var time = (point.Timestamp - startTime).TotalSeconds;
                var value = valueSelector(point);

                var x = chartX + (float)(time / timeRange * chartWidth);
                var y = chartY + chartHeight - (float)((value - minValue) / valueRange * chartHeight);

                if (first)
                {
                    path.MoveTo(x, y);
                    first = false;
                }
                else
                {
                    path.LineTo(x, y);
                }
            }

            canvas.DrawPath(path);
        }
    }

    public class LightSensorChartDrawable : IDrawable
    {
        private readonly List<SensorDataPoint> _data;

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

            var padding = 40f;
            var chartWidth = dirtyRect.Width - (padding * 2);
            var chartHeight = dirtyRect.Height - (padding * 2);
            var chartX = padding;
            var chartY = padding;

            // Calculate time range
            var startTime = _data[0].Timestamp;
            var endTime = _data[_data.Count - 1].Timestamp;
            var timeRange = (endTime - startTime).TotalSeconds;
            if (timeRange < 0.1) timeRange = 0.1;

            // Calculate value range for visible light (X value)
            var visibleValues = _data.Select(d => d.X).ToList();
            var minValue = visibleValues.Min();
            var maxValue = visibleValues.Max();
            
            var valueRange = maxValue - minValue;
            if (valueRange < 0.1) valueRange = 0.1;
            minValue -= valueRange * 0.1f;
            maxValue += valueRange * 0.1f;
            valueRange = maxValue - minValue;

            // Draw axes
            canvas.StrokeColor = Colors.Black;
            canvas.StrokeSize = 1;
            canvas.DrawLine(chartX, chartY + chartHeight, chartX + chartWidth, chartY + chartHeight); // X axis
            canvas.DrawLine(chartX, chartY, chartX, chartY + chartHeight); // Y axis

            // Draw X axis label
            canvas.FontColor = Colors.Black;
            canvas.FontSize = 10;
            var xLabelRect = new RectF(chartX + chartWidth / 2 - 30, chartY + chartHeight + 25, 60, 15);
            canvas.DrawString("Time (s)", xLabelRect, HorizontalAlignment.Center, VerticalAlignment.Top);

            // Draw Y axis label
            canvas.SaveState();
            canvas.Rotate(-90, chartX - 20, chartY + chartHeight / 2);
            var yLabelRect = new RectF(chartX - 20 - 50, chartY + chartHeight / 2 - 10, 100, 20);
            canvas.DrawString("Visible Light", yLabelRect, HorizontalAlignment.Center, VerticalAlignment.Center);
            canvas.RestoreState();

            // Draw visible light series (Orange)
            if (_data.Count > 1)
            {
                canvas.StrokeColor = Colors.Orange;
                canvas.StrokeSize = 2;
                
                var path = new PathF();
                bool first = true;

                for (int i = 0; i < _data.Count; i++)
                {
                    var point = _data[i];
                    var time = (point.Timestamp - startTime).TotalSeconds;
                    var value = point.X;

                    var x = chartX + (float)(time / timeRange * chartWidth);
                    var y = chartY + chartHeight - (float)((value - minValue) / valueRange * chartHeight);

                    if (first)
                    {
                        path.MoveTo(x, y);
                        first = false;
                    }
                    else
                    {
                        path.LineTo(x, y);
                    }
                }

                canvas.DrawPath(path);
            }
        }
    }
}
