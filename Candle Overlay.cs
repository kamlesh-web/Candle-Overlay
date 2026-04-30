using cAlgo.API;
using cAlgo.API.Internals;
using System;
using System.Collections.Generic;

namespace cAlgo
{
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class HigherTimeframeCandleOverlay : Indicator
    {
        [Parameter("Higher Timeframe", Group = "Timeframe")]
        public TimeFrame HigherTimeframe { get; set; }

        [Parameter("Bullish Color", Group = "Colors", DefaultValue = "Green")]
        public Color BullishColor { get; set; }

        [Parameter("Bearish Color", Group = "Colors", DefaultValue = "Red")]
        public Color BearishColor { get; set; }


        [Parameter("Wick Thickness", Group = "Style", DefaultValue = 1, MinValue = 1, MaxValue = 5)]
        public int WickThickness { get; set; }

        [Parameter("Index Offset", Group = "Display", DefaultValue = 0)]
        public int IndexOffset { get; set; }

        private const int VisiblePaddingHigherCandles = 2;

        private Bars _higherBars;
        private readonly HashSet<int> _drawnHigherIndices = new HashSet<int>();

        private int _lastFirstVisible = -1;
        private int _lastLastVisible = -1;
        private int _lastBarsCount = -1;
        private int _lastHigherBarsCount = -1;

        protected override void Initialize()
        {
            if (HigherTimeframe == TimeFrame)
            {
                Print("Higher Timeframe must be different from the chart's timeframe.");
                return;
            }

            _higherBars = MarketData.GetBars(HigherTimeframe);

            Chart.ScrollChanged += OnChartScrollChanged;
            Chart.ZoomChanged += OnChartZoomChanged;

            DrawCandles(forceRedraw: true);
        }

        private void OnChartScrollChanged(ChartScrollEventArgs args)
        {
            DrawCandles();
        }

        private void OnChartZoomChanged(ChartZoomEventArgs args)
        {
            DrawCandles();
        }

        public override void Calculate(int index)
        {
            if (!IsLastBar)
                return;

            var barsCountChanged = Bars.Count != _lastBarsCount;
            var higherBarsCountChanged = _higherBars != null && _higherBars.Count != _lastHigherBarsCount;

            DrawCandles(forceRedraw: barsCountChanged || higherBarsCountChanged);
        }

        private void DrawCandles(bool forceRedraw = false)
        {
            if (_higherBars == null || _higherBars.Count == 0 || Bars.Count == 0)
                return;

            var firstVisibleIndex = Chart.FirstVisibleBarIndex;
            var lastVisibleIndex = Chart.LastVisibleBarIndex;

            if (firstVisibleIndex < 0 || lastVisibleIndex < 0)
                return;

            firstVisibleIndex = Math.Max(0, Math.Min(firstVisibleIndex, Bars.Count - 1));
            lastVisibleIndex = Math.Max(0, Math.Min(lastVisibleIndex, Bars.Count - 1));

            if (firstVisibleIndex > lastVisibleIndex)
            {
                var temp = firstVisibleIndex;
                firstVisibleIndex = lastVisibleIndex;
                lastVisibleIndex = temp;
            }

            if (!forceRedraw &&
                firstVisibleIndex == _lastFirstVisible &&
                lastVisibleIndex == _lastLastVisible &&
                Bars.Count == _lastBarsCount &&
                _higherBars.Count == _lastHigherBarsCount)
            {
                return;
            }

            EnsureHistoryCoverage(Bars.OpenTimes[firstVisibleIndex]);

            var firstHigherIndex = FindLastHigherBarAtOrBefore(Bars.OpenTimes[firstVisibleIndex]);
            var lastHigherIndex = FindLastHigherBarAtOrBefore(Bars.OpenTimes[lastVisibleIndex]);

            if (firstHigherIndex < 0)
                firstHigherIndex = 0;

            if (lastHigherIndex < 0)
                lastHigherIndex = 0;

            if (firstHigherIndex > lastHigherIndex)
            {
                var temp = firstHigherIndex;
                firstHigherIndex = lastHigherIndex;
                lastHigherIndex = temp;
            }

            int drawStart = Math.Max(0, firstHigherIndex - VisiblePaddingHigherCandles);
            int drawEnd = Math.Min(_higherBars.Count - 1, lastHigherIndex + VisiblePaddingHigherCandles);

            var targetIndices = new HashSet<int>();
            int previousEnd = -1;

            for (int h = drawStart; h <= drawEnd; h++)
            {
                var startIndex = FindFirstBaseBarAtOrAfter(_higherBars.OpenTimes[h]) + IndexOffset;
                if (startIndex >= Bars.Count)
                    continue;

                int endIndex;
                if (h == _higherBars.Count - 1)
                {
                    endIndex = Bars.Count - 1 + IndexOffset;
                }
                else
                {
                    endIndex = FindLastBaseBarBefore(_higherBars.OpenTimes[h + 1]) + IndexOffset;
                }

                if (endIndex < 0)
                    continue;

                startIndex = Math.Max(0, startIndex);
                endIndex = Math.Min(Bars.Count - 1, endIndex);

                if (endIndex < startIndex)
                    continue;

                if (startIndex <= previousEnd)
                    startIndex = previousEnd + 1;

                if (startIndex > endIndex)
                    continue;

                previousEnd = endIndex;

                var open = _higherBars.OpenPrices[h];
                var high = _higherBars.HighPrices[h];
                var low = _higherBars.LowPrices[h];
                var close = _higherBars.ClosePrices[h];

                var baseColor = close >= open ? BullishColor : BearishColor;
                var candleColor = baseColor;

                var bodyTop = Math.Max(open, close);
                var bodyBottom = Math.Min(open, close);

                var bodyName = "HTF_body_" + h;
                var wickName = "HTF_wick_" + h;

                var wickRect = Chart.DrawRectangle(wickName, startIndex, low, endIndex, high, candleColor, WickThickness);
                wickRect.IsFilled = false;

                var bodyRect = Chart.DrawRectangle(bodyName, startIndex, bodyBottom, endIndex, bodyTop, candleColor);
                bodyRect.IsFilled = true;

                targetIndices.Add(h);
            }

            foreach (var existing in _drawnHigherIndices)
            {
                if (targetIndices.Contains(existing))
                    continue;

                Chart.RemoveObject("HTF_body_" + existing);
                Chart.RemoveObject("HTF_wick_" + existing);
            }

            _drawnHigherIndices.Clear();
            foreach (var h in targetIndices)
                _drawnHigherIndices.Add(h);

            _lastFirstVisible = firstVisibleIndex;
            _lastLastVisible = lastVisibleIndex;
            _lastBarsCount = Bars.Count;
            _lastHigherBarsCount = _higherBars.Count;
        }

        private void EnsureHistoryCoverage(DateTime requiredStartTime)
        {
            if (_higherBars == null || _higherBars.Count == 0)
                return;

            int safety = 0;
            while (_higherBars.Count > 0 && _higherBars.OpenTimes[0] > requiredStartTime && safety < 200)
            {
                var loaded = _higherBars.LoadMoreHistory();
                if (loaded <= 0)
                    break;

                safety++;
            }
        }

        private int FindLastHigherBarAtOrBefore(DateTime time)
        {
            int left = 0;
            int right = _higherBars.Count - 1;
            int result = -1;

            while (left <= right)
            {
                int mid = left + ((right - left) / 2);
                var midTime = _higherBars.OpenTimes[mid];

                if (midTime <= time)
                {
                    result = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return result;
        }

        private int FindFirstBaseBarAtOrAfter(DateTime time)
        {
            int left = 0;
            int right = Bars.Count - 1;
            int result = Bars.Count;

            while (left <= right)
            {
                int mid = left + ((right - left) / 2);
                var midTime = Bars.OpenTimes[mid];

                if (midTime >= time)
                {
                    result = mid;
                    right = mid - 1;
                }
                else
                {
                    left = mid + 1;
                }
            }

            return result;
        }

        private int FindLastBaseBarBefore(DateTime time)
        {
            int left = 0;
            int right = Bars.Count - 1;
            int result = -1;

            while (left <= right)
            {
                int mid = left + ((right - left) / 2);
                var midTime = Bars.OpenTimes[mid];

                if (midTime < time)
                {
                    result = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return result;
        }
    }
}
