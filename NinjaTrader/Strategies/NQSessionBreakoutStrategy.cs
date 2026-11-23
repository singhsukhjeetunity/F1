// NQSessionBreakoutStrategy.cs
// Standalone chart-visible NQ/MNQ Strategy Analyzer research strategy.
// Plots opening range and approximate RTH VWAP; draws signal brackets and actual fill markers.
// Does NOT instantiate NQSessionBreakout indicator (avoids duplicate indicator dependency).
//
// Research limitations:
// - Bar HLC3 * volume approximates VWAP, not tick-accurate transaction VWAP.
// - OnBarClose signals normally execute at next-bar fills, NOT at signal-bar close.
// - Stop/target price levels are based on the signal-bar close and may differ from realized R.
// - NinjaTrader chart/platform time zone must be Eastern / America/New_York.
// - Requires 5-minute data containing a complete 09:30-09:35 ET opening bar.
// - Research only: NOT approved for live prop-firm automation.

using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;

namespace NinjaTrader.NinjaScript.Strategies
{
    public class NQSessionBreakoutStrategy : Strategy
    {
        private DateTime sessionDate = DateTime.MinValue;
        private int openingBar = -1;
        private double openingHigh;
        private double openingLow;
        private double sumPriceVolume;
        private double sumVolume;
        private int entriesToday;
        private int firstDirection; // +1 long, -1 short
        private int lastExitBar = -1;

        // Drawings belong to this strategy and are separate from the indicator.
        // The plotted entry is the *planned signal close*, not the actual execution fill.
        private string activeTradeTag = "";
        private int activeSignalBar = -1;
        private int activeDirection;
        private double plannedEntry;
        private double plannedStop;
        private double plannedTarget;
        private double actualEntryFill = double.NaN;

        // Execution notifications can occur between bar updates. Buffer markers
        // and render them on the next OnBarUpdate; do not infer fills from OHLC.
        private bool entryMarkerPending;
        private DateTime entryFillTime;
        private double entryFillPrice;
        private string entryMarkerTag = "";

        private bool exitMarkerPending;
        private DateTime exitFillTime;
        private double exitFillPrice;
        private int exitFillResult;
        private string exitMarkerTag = "";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NQSessionBreakoutStrategy";
                Description = "5-minute NQ opening-range breakout, RTH approximate VWAP, 0.5R brackets and chart visuals.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DrawOnPricePanel = true;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 60;
                IsInstantiatedOnEachOptimizationIteration = true;
                BarsRequiredToTrade = 1;
                Contracts = 1;
                LastEntryTime = 153000;
                FlattenTime = 155500;
                MaxStopPoints = 0;
                ShowVisuals = true;

                // These are strategy-owned plots. No duplicate indicator required.
                AddPlot(Brushes.DodgerBlue, "OpeningHigh");
                AddPlot(Brushes.MediumPurple, "OpeningLow");
                AddPlot(Brushes.Gold, "SessionVWAP");
            }
        }

        private void ResetSession(DateTime date)
        {
            sessionDate = date;
            openingBar = -1;
            openingHigh = 0;
            openingLow = 0;
            sumPriceVolume = 0;
            sumVolume = 0;
            entriesToday = 0;
            firstDirection = 0;
            lastExitBar = -1;
        }

        // Update existing trade bracket drawings with the SAME tag each bar.
        // Completed trade lines stop extending once an exit execution is reported.
        private void DrawPlannedBracket(int lastBar)
        {
            if (!ShowVisuals || activeSignalBar < 0 || lastBar < activeSignalBar)
                return;

            int fromAgo = CurrentBar - activeSignalBar;
            int toAgo = CurrentBar - lastBar;

            Draw.Line(this, "F1_ENTRY_" + activeTradeTag,
                fromAgo, plannedEntry, toAgo, plannedEntry, Brushes.SteelBlue);
            Draw.Line(this, "F1_STOP_" + activeTradeTag,
                fromAgo, plannedStop, toAgo, plannedStop, Brushes.Crimson);
            Draw.Line(this, "F1_TARGET_" + activeTradeTag,
                fromAgo, plannedTarget, toAgo, plannedTarget, Brushes.ForestGreen);
        }

        private void DrawSignal(int direction)
        {
            if (!ShowVisuals)
                return;

            if (direction == 1)
                Draw.ArrowUp(this, "F1_LONG_" + activeTradeTag, false,
                    0, Low[0] - 3 * TickSize, Brushes.LimeGreen);
            else
                Draw.ArrowDown(this, "F1_SHORT_" + activeTradeTag, false,
                    0, High[0] + 3 * TickSize, Brushes.Red);

            Draw.Text(this, "F1_LABEL_" + activeTradeTag,
                direction == 1 ? "LONG 0.5R" : "SHORT 0.5R",
                0,
                direction == 1 ? Low[0] - 7 * TickSize : High[0] + 7 * TickSize,
                direction == 1 ? Brushes.LimeGreen : Brushes.Red);

            DrawPlannedBracket(CurrentBar);
        }

        private void RenderPendingExecutions()
        {
            if (ShowVisuals && entryMarkerPending)
            {
                Draw.Dot(this, "F1_FILL_" + entryMarkerTag, false,
                    entryFillTime, entryFillPrice, Brushes.DeepSkyBlue);
            }
            entryMarkerPending = false;

            if (exitMarkerPending)
            {
                if (ShowVisuals)
                {
                    // Bars.GetBar uses the historical bar that corresponds to the
                    // execution timestamp; fall back to the current bar if needed.
                    int endBar = Bars.GetBar(exitFillTime);
                    if (endBar < activeSignalBar || endBar > CurrentBar)
                        endBar = CurrentBar;

                    DrawPlannedBracket(endBar);
                    Brush resultBrush = exitFillResult > 0 ? Brushes.ForestGreen :
                        exitFillResult < 0 ? Brushes.Crimson : Brushes.Orange;
                    Draw.Dot(this, "F1_EXIT_" + exitMarkerTag, false,
                        exitFillTime, exitFillPrice, resultBrush);
                }
                exitMarkerPending = false;
                activeSignalBar = -1;
                activeTradeTag = "";
                actualEntryFill = double.NaN;
            }
            else if (activeSignalBar >= 0)
            {
                DrawPlannedBracket(CurrentBar);
            }
        }

        protected override void OnExecutionUpdate(
            Execution execution,
            string executionId,
            double price,
            int quantity,
            MarketPosition marketPosition,
            string orderId,
            DateTime time)
        {
            if (execution == null || execution.Order == null ||
                execution.Order.OrderState != OrderState.Filled)
                return;

            OrderAction action = execution.Order.OrderAction;

            if ((action == OrderAction.Buy || action == OrderAction.SellShort)
                && marketPosition != MarketPosition.Flat)
            {
                actualEntryFill = price;
                entryFillTime = time;
                entryFillPrice = price;
                entryMarkerTag = activeTradeTag;
                entryMarkerPending = true;
            }
            else if ((action == OrderAction.Sell || action == OrderAction.BuyToCover)
                && marketPosition == MarketPosition.Flat)
            {
                // Preserve the original rule gating: new opposite breakout must
                // close on a subsequent five-minute bar after the exit.
                lastExitBar = CurrentBar;

                double basis = double.IsNaN(actualEntryFill)
                    ? plannedEntry : actualEntryFill;
                double pnlPoints = activeDirection * (price - basis);
                exitFillResult = pnlPoints > 0 ? 1 : pnlPoints < 0 ? -1 : 0;
                exitFillTime = time;
                exitFillPrice = price;
                exitMarkerTag = activeTradeTag;
                exitMarkerPending = true;
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || CurrentBar < 1)
                return;

            if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute ||
                BarsPeriod.Value != 5)
                return;

            // Clear plots on every bar to prevent connecting across dates or
            // drawing invalid zero-valued levels outside the cash session.
            Values[0][0] = double.NaN;
            Values[1][0] = double.NaN;
            Values[2][0] = double.NaN;

            RenderPendingExecutions();

            int clock = ToTime(Time[0]);
            if (Time[0].Date != sessionDate)
                ResetSession(Time[0].Date);

            // NinjaTrader stamps the bar by its closing time in this series.
            if (clock == 93500)
            {
                ResetSession(Time[0].Date);
                openingBar = CurrentBar;
                openingHigh = High[0];
                openingLow = Low[0];
            }

            if (openingBar < 0 || clock < 93500)
                return;

            // RTH-anchored HLC3 * bar-volume approximation of VWAP.
            if (clock <= 160000)
            {
                double typical = (High[0] + Low[0] + Close[0]) / 3.0;
                sumPriceVolume += typical * Volume[0];
                sumVolume += Volume[0];

                if (ShowVisuals)
                {
                    Values[0][0] = openingHigh;
                    Values[1][0] = openingLow;
                    if (sumVolume > 0)
                        Values[2][0] = sumPriceVolume / sumVolume;
                }
            }

            // No changes to trading conditions below: the drawing code is
            // independent of the research strategy's entry/exit decisions.
            if (clock >= FlattenTime)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    ExitLong("ResearchTimeExit", "Long1");
                else if (Position.MarketPosition == MarketPosition.Short)
                    ExitShort("ResearchTimeExit", "Short1");
                return;
            }

            if (clock > LastEntryTime ||
                CurrentBar <= openingBar ||
                sumVolume <= 0 ||
                entriesToday >= 2 ||
                Position.MarketPosition != MarketPosition.Flat ||
                lastExitBar >= CurrentBar)
                return;

            double vwap = sumPriceVolume / sumVolume;
            bool goLong = Close[1] <= openingHigh &&
                Close[0] > openingHigh && Close[0] > vwap;
            bool goShort = Close[1] >= openingLow &&
                Close[0] < openingLow && Close[0] < vwap;

            int direction = goLong ? 1 : goShort ? -1 : 0;
            if (direction == 0 ||
                (entriesToday == 1 && direction == firstDirection))
                return;

            double candidateEntry = Close[0];
            double stop = Instrument.MasterInstrument.RoundToTickSize(
                direction == 1 ? Low[0] : High[0]);
            double risk = direction == 1
                ? candidateEntry - stop : stop - candidateEntry;

            if (risk < TickSize ||
                (MaxStopPoints > 0 && risk > MaxStopPoints))
                return;

            double target = Instrument.MasterInstrument.RoundToTickSize(
                candidateEntry + direction * 0.5 * risk);
            if (direction == 1 ? target <= candidateEntry : target >= candidateEntry)
                return;

            string entryName = direction == 1 ? "Long1" : "Short1";
            SetStopLoss(entryName, CalculationMode.Price, stop, false);
            SetProfitTarget(entryName, CalculationMode.Price, target);

            // Record the planned bracket before submitting the actual orders.
            // Strategy Analyzer may execute on the next bar with a different
            // fill price, which is represented by blue entry execution dots.
            activeTradeTag = sessionDate.ToString("yyyyMMdd") + "_" + (entriesToday + 1);
            activeSignalBar = CurrentBar;
            activeDirection = direction;
            plannedEntry = candidateEntry;
            plannedStop = stop;
            plannedTarget = target;
            actualEntryFill = double.NaN;
            DrawSignal(direction);

            if (direction == 1)
                EnterLong(Contracts, entryName);
            else
                EnterShort(Contracts, entryName);

            entriesToday++;
            if (entriesToday == 1)
                firstDirection = direction;
        }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contracts", GroupName = "Parameters", Order = 1)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Range(93500, 155500)]
        [Display(Name = "Last entry time (ET HHmmss)", GroupName = "Parameters", Order = 2)]
        public int LastEntryTime { get; set; }

        [NinjaScriptProperty]
        [Range(93500, 160000)]
        [Display(Name = "Flatten time (ET HHmmss)", GroupName = "Parameters", Order = 3)]
        public int FlattenTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Display(Name = "Max stop points (0 disables)", GroupName = "Parameters", Order = 4)]
        public double MaxStopPoints { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show chart visuals", GroupName = "Parameters", Order = 5)]
        public bool ShowVisuals { get; set; }
    }
}
