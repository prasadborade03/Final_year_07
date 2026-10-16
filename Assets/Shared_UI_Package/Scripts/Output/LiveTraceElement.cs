using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectName.Output
{
    public class LiveTraceElement : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<LiveTraceElement, UxmlTraits> { }

        public static readonly Color[] ChannelColors = new Color[]
        {
            new Color(0.0f, 0.898f, 1.0f, 1.0f),   // Speed: Cyan (#00E5FF)
            new Color(1.0f, 0.702f, 0.0f, 1.0f),   // Slope: Amber (#FFB300)
            new Color(0.0f, 0.902f, 0.463f, 1.0f),  // Slip: Green (#00E676)
            new Color(1.0f, 0.090f, 0.267f, 1.0f),  // Power: Red (#FF1744)
            new Color(0.0f, 0.690f, 1.0f, 1.0f),   // Battery: Blue (#00B0FF)
            new Color(1.0f, 0.427f, 0.0f, 1.0f)    // Motor Temp: Orange (#FF6D00)
        };

        public static readonly string[] ChannelNames = new string[]
        {
            "SPEED", "SLOPE", "SLIP", "POWER", "BATT", "TEMP"
        };

        public static readonly string[] ChannelUnits = new string[]
        {
            "m/s", "deg", "%", "W", "%", "°C"
        };

        public bool[] ChannelEnabled = new bool[] { true, true, true, false, false, false };

        private VisualElement canvas;
        private VisualElement yAxisOverlay;
        private Label[] yMinLabels;
        private Label[] yMaxLabels;
        private VisualElement xAxisRow;
        private Label lblEmpty;

        public LiveTraceElement()
        {
            style.flexGrow = 1;
            style.position = Position.Relative;
            style.overflow = Overflow.Hidden;

            // Background canvas for Painter2D
            canvas = new VisualElement();
            canvas.style.position = Position.Absolute;
            canvas.style.left = 40;
            canvas.style.right = 10;
            canvas.style.top = 10;
            canvas.style.bottom = 24;
            canvas.generateVisualContent += OnGenerateVisualContent;
            Add(canvas);

            // Left Y-axis overlay
            yAxisOverlay = new VisualElement();
            yAxisOverlay.style.position = Position.Absolute;
            yAxisOverlay.style.left = 4;
            yAxisOverlay.style.top = 10;
            yAxisOverlay.style.bottom = 24;
            yAxisOverlay.style.width = 34;
            yAxisOverlay.style.justifyContent = Justify.SpaceBetween;
            yAxisOverlay.pickingMode = PickingMode.Ignore;

            var yMaxContainer = new VisualElement();
            yMaxContainer.style.flexDirection = FlexDirection.Column;
            var yMinContainer = new VisualElement();
            yMinContainer.style.flexDirection = FlexDirection.Column;

            yMinLabels = new Label[6];
            yMaxLabels = new Label[6];

            for (int i = 0; i < 6; i++)
            {
                var lblMax = new Label();
                lblMax.style.fontSize = 9;
                lblMax.style.color = ChannelColors[i];
                lblMax.style.unityFontStyleAndWeight = FontStyle.Bold;
                lblMax.style.display = ChannelEnabled[i] ? DisplayStyle.Flex : DisplayStyle.None;
                yMaxContainer.Add(lblMax);
                yMaxLabels[i] = lblMax;

                var lblMin = new Label();
                lblMin.style.fontSize = 9;
                lblMin.style.color = ChannelColors[i];
                lblMin.style.display = ChannelEnabled[i] ? DisplayStyle.Flex : DisplayStyle.None;
                yMinContainer.Add(lblMin);
                yMinLabels[i] = lblMin;
            }

            yAxisOverlay.Add(yMaxContainer);
            yAxisOverlay.Add(yMinContainer);
            Add(yAxisOverlay);

            // Bottom X-axis row (-60s to 0s)
            xAxisRow = new VisualElement();
            xAxisRow.style.position = Position.Absolute;
            xAxisRow.style.left = 40;
            xAxisRow.style.right = 10;
            xAxisRow.style.bottom = 4;
            xAxisRow.style.height = 18;
            xAxisRow.style.flexDirection = FlexDirection.Row;
            xAxisRow.style.justifyContent = Justify.SpaceBetween;
            xAxisRow.pickingMode = PickingMode.Ignore;

            string[] times = new string[] { "-60s", "-45s", "-30s", "-15s", "0s" };
            foreach (var t in times)
            {
                var lblTime = new Label(t);
                lblTime.style.fontSize = 10;
                lblTime.style.color = new Color(0.5f, 0.87f, 0.92f, 0.7f);
                xAxisRow.Add(lblTime);
            }
            Add(xAxisRow);

            // Centered empty state label
            lblEmpty = new Label("No data. Start recording.");
            lblEmpty.style.position = Position.Absolute;
            lblEmpty.style.left = 0;
            lblEmpty.style.right = 0;
            lblEmpty.style.top = 0;
            lblEmpty.style.bottom = 0;
            lblEmpty.style.unityTextAlign = TextAnchor.MiddleCenter;
            lblEmpty.style.color = new Color(0.5f, 0.87f, 0.92f, 0.5f);
            lblEmpty.style.fontSize = 14;
            lblEmpty.pickingMode = PickingMode.Ignore;
            Add(lblEmpty);
        }

        public void SetChannelEnabled(int index, bool enabled)
        {
            if (index < 0 || index >= ChannelEnabled.Length) return;
            ChannelEnabled[index] = enabled;
            if (yMinLabels[index] != null) yMinLabels[index].style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
            if (yMaxLabels[index] != null) yMaxLabels[index].style.display = enabled ? DisplayStyle.Flex : DisplayStyle.None;
            canvas.MarkDirtyRepaint();
        }

        public void RefreshTrace()
        {
            var recorder = MissionRecorder.Instance;
            bool hasData = recorder != null && recorder.TraceBuffers != null && recorder.TraceBuffers[0].Count > 1;

            lblEmpty.style.display = hasData ? DisplayStyle.None : DisplayStyle.Flex;
            yAxisOverlay.style.display = hasData ? DisplayStyle.Flex : DisplayStyle.None;
            xAxisRow.style.display = hasData ? DisplayStyle.Flex : DisplayStyle.None;

            if (hasData)
            {
                for (int i = 0; i < 6; i++)
                {
                    if (ChannelEnabled[i])
                    {
                        recorder.TraceBuffers[i].GetMinMax(out float min, out float max);
                        yMaxLabels[i].text = max.ToString("F1");
                        yMinLabels[i].text = min.ToString("F1");
                    }
                }
            }

            canvas.MarkDirtyRepaint();
        }

        private void OnGenerateVisualContent(MeshGenerationContext mgc)
        {
            var painter = mgc.painter2D;
            Rect rect = canvas.contentRect;
            if (rect.width <= 10f || rect.height <= 10f) return;

            // Draw Subtle Grid
            Color gridColor = new Color(0f, 0.898f, 1f, 0.15f);
            painter.strokeColor = gridColor;
            painter.lineWidth = 1f;

            // Horizontal grid lines (4 divisions)
            for (int i = 0; i <= 4; i++)
            {
                float y = rect.y + (rect.height * i / 4f);
                painter.BeginPath();
                painter.MoveTo(new Vector2(rect.x, y));
                painter.LineTo(new Vector2(rect.x + rect.width, y));
                painter.Stroke();
            }

            // Vertical grid lines (4 divisions = 15s intervals)
            for (int j = 0; j <= 4; j++)
            {
                float x = rect.x + (rect.width * j / 4f);
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, rect.y));
                painter.LineTo(new Vector2(x, rect.y + rect.height));
                painter.Stroke();
            }

            var recorder = MissionRecorder.Instance;
            if (recorder == null || recorder.TraceBuffers == null) return;

            for (int ch = 0; ch < 6; ch++)
            {
                if (!ChannelEnabled[ch]) continue;

                var buffer = recorder.TraceBuffers[ch];
                int count = buffer.Count;
                if (count < 2) continue;

                buffer.GetMinMax(out float min, out float max);
                float range = max - min;
                if (range <= 0.0001f) range = 1f;

                painter.strokeColor = ChannelColors[ch];
                painter.lineWidth = 1.8f;
                painter.BeginPath();

                float stepX = rect.width / (buffer.Capacity - 1);
                int startIndex = buffer.Capacity - count;

                for (int i = 0; i < count; i++)
                {
                    float val = buffer.Get(i);
                    float normY = Mathf.Clamp01((val - min) / range);
                    float px = rect.x + (startIndex + i) * stepX;
                    float py = rect.y + rect.height - (normY * rect.height);

                    if (i == 0)
                    {
                        painter.MoveTo(new Vector2(px, py));
                    }
                    else
                    {
                        painter.LineTo(new Vector2(px, py));
                    }
                }

                painter.Stroke();
            }
        }
    }
}
