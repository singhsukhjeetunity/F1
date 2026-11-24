// NQSessionBreakoutStrategy.cs
// Research/backtest companion to the original NQSessionBreakout indicator.
// Does NOT invoke indicator virtual-trade logic: real strategy Position is authoritative.
// Chart/platform time zone must be America/New_York (Eastern Time).
// Requires 5-minute NQ/MNQ bars including the 09:30-09:35 NY opening bar.
//
// Research limitations:
// - Uses cumulative typical-price * 5-minute-volume VWAP approximation (not tick VWAP).
// - Signals at confirmed bar close; market entries fill on the NEXT bar/tick in a backtest.
// - Stops and targets reference the SIGNAL BAR CLOSE, not necessarily the eventual fill.
// - Trade quantity is determined from a fixed planned-stop risk budget.
// - This is a cap on PLANNED stop risk, not a guarantee of realized fills.
// - Test with realistic data, commissions, slippage and intrabar fill resolution.
// - NOT validated for unattended live execution or any prop-firm compliance.

using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;

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
        private int firstDirection; // 1 = long, -1 = short
        private int lastExitBar = -1;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NQSessionBreakoutStrategy";
                Description = "Independent 5-minute NQ opening-range breakout with RTH approximate VWAP and 0.5R brackets.";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.UniqueEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 60;
                IsInstantiatedOnEachOptimizationIteration = true;
                BarsRequiredToTrade = 1;
                FixedRiskDollars = 100.0;
                MaxContracts = 20;
                CostReservePerContract = 0.0;
                LastEntryTime = 153000;
                FlattenTime = 155500;
                MaxStopPoints = 0;
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

            // Only a completed exit permits an opposite-side second entry.
            // The new confirmation must also be from a later finished 5-minute bar.
            OrderAction action = execution.Order.OrderAction;
            if ((action == OrderAction.Sell || action == OrderAction.BuyToCover) &&
                marketPosition == MarketPosition.Flat)
                lastExitBar = CurrentBar;
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || CurrentBar < 1)
                return;

            if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value != 5)
                return;

            int clock = ToTime(Time[0]);
            if (Time[0].Date != sessionDate)
                ResetSession(Time[0].Date);

            if (clock == 93500)
            {
                ResetSession(Time[0].Date);
                openingBar = CurrentBar;
                openingHigh = High[0];
                openingLow = Low[0];
            }

            if (openingBar < 0 || clock < 93500)
                return;

            // VWAP approximation reset at the 09:30-09:35 NY candle.
            if (clock <= 160000)
            {
                double typical = (High[0] + Low[0] + Close[0]) / 3.0;
                sumPriceVolume += typical * Volume[0];
                sumVolume += Volume[0];
            }

            // Research-only NY cash-session flatten safeguard.
            // With OnBarClose, the exit is submitted after this bar's close,
            // normally filling on the subsequent bar. Verify trading-hours template.
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

            // Require a fresh, strict cross, not just a wick touching the level.
            double vwap = sumPriceVolume / sumVolume;
            bool goLong = Close[1] <= openingHigh && Close[0] > openingHigh && Close[0] > vwap;
            bool goShort = Close[1] >= openingLow && Close[0] < openingLow && Close[0] < vwap;
            int direction = goLong ? 1 : goShort ? -1 : 0;
            if (direction == 0 || (entriesToday == 1 && direction == firstDirection))
                return;

            // Use the confirmation bar CLOSE as the planned entry price, as in
            // the provided video. Actual Strategy Analyzer market fill differs.
            double plannedEntry = Close[0];
            double stop = Instrument.MasterInstrument.RoundToTickSize(
                direction == 1 ? Low[0] : High[0]);
            double risk = direction == 1 ? plannedEntry - stop : stop - plannedEntry;
            if (risk < TickSize || (MaxStopPoints > 0 && risk > MaxStopPoints))
                return;

            double target = Instrument.MasterInstrument.RoundToTickSize(
                plannedEntry + direction * 0.5 * risk);
            if (direction == 1 ? target <= plannedEntry : target >= plannedEntry)
                return;

            // Size from the stop's distance in points multiplied by the cash
            // value of one futures point for this instrument (MNQ: $2, NQ: $20).
            // Use only the strategy's PLANNED signal-close entry price.
            // Any next-bar gap, slippage, commission or stop-market overfill
            // can make ACTUAL losses exceed FixedRiskDollars.
            double pointValue = Instrument.MasterInstrument.PointValue;
            if (pointValue <= 0 || double.IsNaN(pointValue))
                return;

            double plannedLossPerContract = risk * pointValue + CostReservePerContract;
            if (plannedLossPerContract <= 0 ||
                double.IsNaN(plannedLossPerContract) ||
                double.IsInfinity(plannedLossPerContract))
                return;

            int quantity = (int)Math.Floor(
                Math.Min((double)MaxContracts, FixedRiskDollars / plannedLossPerContract));

            // Skip rather than forcing one contract and exceeding the cap.
            if (quantity < 1)
                return;

            string entryName = direction == 1 ? "Long1" : "Short1";
            // NinjaTrader managed stop/target orders are OCO-linked.
            // Target is anchored to planned close, NOT the next-bar fill.
            SetStopLoss(entryName, CalculationMode.Price, stop, false);
            SetProfitTarget(entryName, CalculationMode.Price, target);

            if (direction == 1)
                EnterLong(quantity, entryName);
            else
                EnterShort(quantity, entryName);

            entriesToday++;
            if (entriesToday == 1)
                firstDirection = direction;
        }

        [NinjaScriptProperty]
        [Range(1.0, 1000000.0)]
        [Display(Name = "Fixed risk dollars per trade", GroupName = "Parameters", Order = 1)]
        public double FixedRiskDollars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Maximum contracts", GroupName = "Parameters", Order = 2)]
        public int MaxContracts { get; set; }

        [NinjaScriptProperty]
        [Range(0.0, 1000.0)]
        [Display(Name = "Cost reserve per contract ($)", GroupName = "Parameters", Order = 3)]
        public double CostReservePerContract { get; set; }

        [NinjaScriptProperty]
        [Range(93500, 155500)]
        [Display(Name = "Last entry time (ET HHmmss)", GroupName = "Parameters", Order = 4)]
        public int LastEntryTime { get; set; }

        [NinjaScriptProperty]
        [Range(93500, 160000)]
        [Display(Name = "Flatten time (ET HHmmss)", GroupName = "Parameters", Order = 5)]
        public int FlattenTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100000)]
        [Display(Name = "Max stop points (0 disables)", GroupName = "Parameters", Order = 6)]
        public double MaxStopPoints { get; set; }
    }
}
