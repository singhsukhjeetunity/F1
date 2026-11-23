// NQSessionBreakout.cs
// Independent educational indicator based on the rules described in the user-supplied video transcript.
// NinjaTrader 8, 5-minute NQ/MNQ bars, application time zone set to Eastern (New York).
// IMPORTANT: Bar-approximated RTH VWAP, NOT tick-accurate exchange VWAP.
// IMPORTANT: Virtual stops/targets are inferred from bar high/low, NOT broker executions.

using System;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class NQSessionBreakout : Indicator
    {
        private DateTime day = DateTime.MinValue;
        private double rangeHigh;
        private double rangeLow;
        private double cumulativePV;
        private double cumulativeVolume;
        private double previousVwap;
        private int openingBar = -1;
        private int tradesToday;
        private int firstDirection;  // +1 for long, -1 for short
        private int activeDirection; // 0 means no virtual position
        private int entryBar = -1;
        private int lastExitBar = -1;
        private double entryPrice;
        private double stopPrice;
        private double targetPrice;
        private string tradeTag;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NQSessionBreakout";
                Description = "9:30 ET NQ opening range, RTH VWAP filter, breakout signal and 0.5R bracket visualization";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DrawOnPricePanel = true;
                IsSuspendedWhileInactive = true;
            }
        }

        private void ResetDay(DateTime date)
        {
            day = date;
            rangeHigh = 0;
            rangeLow = 0;
            cumulativePV = 0;
            cumulativeVolume = 0;
            previousVwap = 0;
            openingBar = -1;
            tradesToday = 0;
            firstDirection = 0;
            activeDirection = 0;
            entryBar = -1;
            lastExitBar = -1;
            tradeTag = "";
        }

        private void DrawTradeLevels()
        {
            if (entryBar < 0 || entryBar > CurrentBar)
                return;

            int from = CurrentBar - entryBar;
            Draw.Line(this, "ENTRY_" + tradeTag, from, entryPrice, 0, entryPrice, Brushes.SteelBlue);
            Draw.Line(this, "STOP_" + tradeTag, from, stopPrice, 0, stopPrice, Brushes.Crimson);
            Draw.Line(this, "TARGET_" + tradeTag, from, targetPrice, 0, targetPrice, Brushes.ForestGreen);
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || CurrentBar < 1)
                return;

            // Signal timestamps assume exactly 5-minute time-based bars.
            if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value != 5)
                return;

            // NinjaTrader's Time[0] is the *closing* timestamp of a 5-minute bar.
            // 09:35 represents trades from 09:30:00 through 09:34:59 ET.
            int clock = ToTime(Time[0]);
            if (Time[0].Date != day)
                ResetDay(Time[0].Date);

            if (clock == 93500)
            {
                // Start a new NY cash session and anchor the VWAP here.
                ResetDay(Time[0].Date);
                openingBar = CurrentBar;
                rangeHigh = High[0];
                rangeLow = Low[0];
            }

            if (openingBar < 0 || clock < 93500 || clock > 160000)
                return;

            // Five-minute HLC3 x volume approximation of RTH-anchored VWAP.
            // Requires a tick-based implementation to match true transaction VWAP.
            double typicalPrice = (High[0] + Low[0] + Close[0]) / 3.0;
            cumulativePV += typicalPrice * (double)Volume[0];
            cumulativeVolume += (double)Volume[0];
            if (cumulativeVolume <= 0)
                return;
            double vwap = cumulativePV / cumulativeVolume;
            string dateTag = day.ToString("yyyyMMdd");

            // Preserve levels for each historical session; extend as bars arrive.
            Draw.Line(this, "OR_HIGH_" + dateTag, CurrentBar - openingBar, rangeHigh,
                0, rangeHigh, Brushes.DodgerBlue);
            Draw.Line(this, "OR_LOW_" + dateTag, CurrentBar - openingBar, rangeLow,
                0, rangeLow, Brushes.MediumPurple);

            if (CurrentBar > openingBar)
                Draw.Line(this, "VWAP_" + dateTag + "_" + CurrentBar,
                    1, previousVwap, 0, vwap, Brushes.Gold);
            previousVwap = vwap;

            // Chart-only virtual position tracking for deciding whether an
            // opposite-direction SECOND signal is eligible. Stop wins on OHLC
            // ambiguity when both target and stop were touched in one bar.
            if (activeDirection != 0 && CurrentBar > entryBar)
            {
                DrawTradeLevels();
                bool stopped = activeDirection == 1
                    ? Low[0] <= stopPrice : High[0] >= stopPrice;
                bool targeted = activeDirection == 1
                    ? High[0] >= targetPrice : Low[0] <= targetPrice;

                if (stopped || targeted)
                {
                    bool won = !stopped && targeted;
                    Draw.Dot(this, "EXIT_" + tradeTag, false, 0,
                        stopped ? stopPrice : targetPrice,
                        won ? Brushes.ForestGreen : Brushes.Crimson);
                    activeDirection = 0;
                    lastExitBar = CurrentBar;
                }
            }

            if (activeDirection != 0 || lastExitBar == CurrentBar || tradesToday >= 2 ||
                CurrentBar <= openingBar)
                return;

            // A fresh CLOSE through a fixed opening-range boundary; wicks and
            // equality do not qualify. VWAP must be on the same side.
            bool longSignal = Close[1] <= rangeHigh && Close[0] > rangeHigh && Close[0] > vwap;
            bool shortSignal = Close[1] >= rangeLow && Close[0] < rangeLow && Close[0] < vwap;
            int direction = longSignal ? 1 : shortSignal ? -1 : 0;

            // Only the second, OPPOSITE-side breakout is permitted.
            if (direction == 0 || (tradesToday == 1 && direction == firstDirection))
                return;

            double proposedEntry = Close[0];
            double proposedStop = direction == 1 ? Low[0] : High[0];
            double riskPoints = Math.Abs(proposedEntry - proposedStop);
            if (riskPoints < TickSize)
                return;

            double proposedTarget = Instrument.MasterInstrument.RoundToTickSize(
                proposedEntry + direction * 0.5 * riskPoints);

            tradesToday++;
            if (tradesToday == 1)
                firstDirection = direction;
            activeDirection = direction;
            entryBar = CurrentBar;
            entryPrice = proposedEntry;
            stopPrice = proposedStop;
            targetPrice = proposedTarget;
            tradeTag = dateTag + "_" + tradesToday;

            if (direction == 1)
                Draw.ArrowUp(this, "LONG_" + tradeTag, false, 0,
                    Low[0] - 3 * TickSize, Brushes.LimeGreen);
            else
                Draw.ArrowDown(this, "SHORT_" + tradeTag, false, 0,
                    High[0] + 3 * TickSize, Brushes.Red);

            Draw.Text(this, "LABEL_" + tradeTag,
                direction == 1 ? "LONG 0.5R" : "SHORT 0.5R", 0,
                direction == 1 ? Low[0] - 7 * TickSize : High[0] + 7 * TickSize,
                direction == 1 ? Brushes.LimeGreen : Brushes.Red);
            DrawTradeLevels();
        }
    }
}
