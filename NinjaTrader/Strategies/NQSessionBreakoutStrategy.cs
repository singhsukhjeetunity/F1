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

        // Live-safety state: historical entry/exit rules remain unchanged.
        private bool entryPending;
        private bool liveInstrumentAllowed;
        private bool liveFault;
        // Transient connection availability is separate from latched order faults.
        private bool liveConnectionReady = true;
        private bool missingOpenBarLogged;
        private bool suppressCurrentSession;
        private double plannedEntryPrice;
        private double plannedStopPrice;
        private int plannedDirection, plannedQuantity;


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

                // Platform-managed error and disconnection behavior.
                StartBehavior = StartBehavior.WaitUntilFlat;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                ConnectionLossHandling = ConnectionLossHandling.StopStrategy;
                DisconnectDelaySeconds = 10;
                NumberRestartAttempts = 0;

                // Route diagnostics to Output 1; key events also go to Control Center Log.
                PrintTo = PrintTo.OutputTab1;
                // Native NinjaTrader Enabled governs execution.
                MaxAdverseEntrySlippageTicks = 4;
                MNQOnlyWhenLive = true;
            }
            else if (State == State.Realtime)
            {
                // Realtime can begin on an already-connected adapter; a later
                // connection event will update this provisional ready state.
                liveConnectionReady = true;
                liveInstrumentAllowed = !MNQOnlyWhenLive
                    || Instrument.MasterInstrument.Name == "MNQ";

                if (CurrentBar >= 0 && ToTime(Time[0]) >= 93500
                    && ToTime(Time[0]) < 160000)
                    suppressCurrentSession = true; // No mid-session startup reentry.

                Diag("START account=" + (Account == null ? "unknown" : Account.Name)
                    + " symbol=" + Instrument.FullName
                    + " instrumentAllowed=" + liveInstrumentAllowed
                    + " connectionReady=" + liveConnectionReady
                    + " midSessionBlocked=" + suppressCurrentSession
                    + " riskBudget=" + FixedRiskDollars
                    + " maxContracts=" + MaxContracts, true);
            }
        }

        private void Diag(string message, bool toControlCenter = false)
        {
            if (State != State.Realtime)
                return;

            string line = "[F1] " + DateTime.Now.ToString("HH:mm:ss")
                + " " + message;
            Print(line);
            if (toControlCenter)
                Log(line, LogLevel.Information);
        }

        private void EmergencyStop(string reason)
        {
            if (liveFault) return;
            liveFault = true;
            Diag("EMERGENCY STOP: " + reason, true);
            // Only close the position managed by this strategy.
            if (State == State.Realtime)
                CloseStrategy("F1EmergencyClose");
        }

        protected override void OnConnectionStatusUpdate(
            ConnectionStatusEventArgs update)
        {
            if (State != State.Realtime || update == null)
                return;

            // Temporary adapter transitions block entries only while the
            // connection is not ready. They must NOT latch liveFault.
            // NinjaTrader's StopStrategy policy still handles a sustained loss.
            bool ready = update.Status == ConnectionStatus.Connected
                && update.PriceStatus == ConnectionStatus.Connected;
            if (ready != liveConnectionReady)
            {
                liveConnectionReady = ready;
                Diag("CONNECTION " + (ready ? "RECOVERED" : "NOT READY")
                    + " orderFeed=" + update.Status
                    + " priceFeed=" + update.PriceStatus
                    + " (platform StopStrategy remains enabled)", true);
            }
        }

        protected override void OnOrderUpdate(Order order, double limitPrice,
            double stopPrice, int quantity, int filled, double averageFillPrice,
            OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            if (State != State.Realtime || order == null) return;
            if (orderState == OrderState.Rejected || error != ErrorCode.NoError)
            {
                liveFault = true;
                Diag("ORDER ERROR " + order.Name + " " + orderState +
                    " " + error + " " + comment +
                    "; platform StopCancelClose remains enabled", true);
            }
            if ((order.Name == "Long1" || order.Name == "Short1")
                && orderState == OrderState.Cancelled)
            {
                liveFault = true;
                Diag("ENTRY CANCELLED: manual order/position reconciliation required", true);
            }
        }

        private bool RealtimeAccountSafe()
        {
            // Safeguard against collisions with manual/other strategy positions.
            return liveInstrumentAllowed && liveConnectionReady
                && !liveFault && !suppressCurrentSession
                && Account != null && PositionAccount != null
                && PositionAccount.MarketPosition == MarketPosition.Flat;
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
            entryPending = false;
            missingOpenBarLogged = false;
            // liveFault deliberately persists until manual intervention.
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
            if (action == OrderAction.Buy || action == OrderAction.SellShort)
            {
                if (execution.Order.OrderState == OrderState.Filled)
                    entryPending = false;

                if (State == State.Realtime)
                {
                    Diag("ENTRY FILL: " + action + " qty=" + quantity
                        + " price=" + price.ToString("F2"));
                    if (liveInstrumentAllowed && !liveFault)
                    {
                        double adverse = plannedDirection == 1
                            ? (price - plannedEntryPrice) / TickSize
                            : (plannedEntryPrice - price) / TickSize;
                        double riskAtFill = Math.Abs(price - plannedStopPrice) *
                            Instrument.MasterInstrument.PointValue * plannedQuantity +
                            CostReservePerContract * plannedQuantity;
                        if (adverse > MaxAdverseEntrySlippageTicks ||
                            riskAtFill > FixedRiskDollars + 0.01)
                            EmergencyStop("Adverse fill/risk over budget: ticks="
                                + adverse.ToString("F1") + " risk="
                                + riskAtFill.ToString("F2"));
                    }
                }
            }
            else if ((action == OrderAction.Sell || action == OrderAction.BuyToCover) &&
                marketPosition == MarketPosition.Flat)
            {
                lastExitBar = CurrentBar;
                Diag("EXIT FILL: " + action + " qty=" + quantity
                    + " price=" + price.ToString("F2"));
            }
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
                suppressCurrentSession = false;
                if (State == State.Realtime)
                    Diag("NY SESSION reset time=" + Time[0].ToString("HH:mm:ss")
                        + " openingHigh=" + openingHigh.ToString("F2")
                        + " openingLow=" + openingLow.ToString("F2")
                        + " instrumentAllowed=" + liveInstrumentAllowed
                        + " connectionReady=" + liveConnectionReady
                        + " liveFault=" + liveFault, true);
            }

            if (openingBar < 0 || clock < 93500)
            {
                if (State == State.Realtime && clock >= 93500
                    && clock <= 103000 && !missingOpenBarLogged)
                {
                    missingOpenBarLogged = true;
                    Diag("BLOCKED: missing the 09:35 ET opening-range bar; "
                        + "barTime=" + Time[0].ToString("HH:mm:ss"), true);
                }
                return;
            }

            // VWAP approximation reset at the 09:30-09:35 NY candle.
            if (clock <= 160000)
            {
                double typical = (High[0] + Low[0] + Close[0]) / 3.0;
                sumPriceVolume += typical * Volume[0];
                sumVolume += Volume[0];
            }

            // Live-only diagnostics: show each 5-minute bar through 10:30 ET,
            // and any potential breakout later in the session. All signal
            // calculations and historical orders below are unchanged.
            if (State == State.Realtime)
            {
                double diagnosticVwap = sumVolume > 0
                    ? sumPriceVolume / sumVolume : double.NaN;
                bool possibleLong = CurrentBar > openingBar && sumVolume > 0
                    && Close[1] <= openingHigh && Close[0] > openingHigh
                    && Close[0] > diagnosticVwap;
                bool possibleShort = CurrentBar > openingBar && sumVolume > 0
                    && Close[1] >= openingLow && Close[0] < openingLow
                    && Close[0] < diagnosticVwap;

                if ((clock >= 93500 && clock <= 103000)
                    || possibleLong || possibleShort)
                {
                    Diag("BAR timeET=" + Time[0].ToString("HH:mm:ss")
                        + " close=" + Close[0].ToString("F2")
                        + " priorClose=" + Close[1].ToString("F2")
                        + " openHi=" + openingHigh.ToString("F2")
                        + " openLo=" + openingLow.ToString("F2")
                        + " vwap=" + diagnosticVwap.ToString("F2")
                        + " long=" + possibleLong + " short=" + possibleShort
                        + " connReady=" + liveConnectionReady
                        + " fault=" + liveFault
                        + " startupBlocked=" + suppressCurrentSession
                        + " acctFlat=" + (PositionAccount != null
                            && PositionAccount.MarketPosition == MarketPosition.Flat)
                        + " entryPending=" + entryPending
                        + " entriesToday=" + entriesToday);
                }

                if ((possibleLong || possibleShort) && !RealtimeAccountSafe())
                    Diag("BLOCKED breakout at " + Time[0].ToString("HH:mm:ss")
                        + ": connectionReady=" + liveConnectionReady
                        + ", instrumentAllowed=" + liveInstrumentAllowed
                        + ", liveFault=" + liveFault
                        + ", midSessionStartup=" + suppressCurrentSession
                        + ", accountAvailable=" + (Account != null)
                        + ", accountFlat=" + (PositionAccount != null
                            && PositionAccount.MarketPosition == MarketPosition.Flat),
                        true);
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

            // Historical entry logic remains untouched. Native Enabled controls
            // live execution; faults and conflicting account positions block entries.
            if (State == State.Realtime && !RealtimeAccountSafe())
                return;

            if (clock > LastEntryTime ||
                CurrentBar <= openingBar ||
                sumVolume <= 0 ||
                entriesToday >= 2 ||
                entryPending ||
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
            {
                if (State == State.Realtime)
                    Diag("BLOCKED breakout: risk budget " + FixedRiskDollars
                        + " cannot cover one contract; stopPoints="
                        + risk.ToString("F2") + " perContractRisk="
                        + plannedLossPerContract.ToString("F2"), true);
                return;
            }

            string entryName = direction == 1 ? "Long1" : "Short1";
            // NinjaTrader managed stop/target orders are OCO-linked.
            // Target is anchored to planned close, NOT the next-bar fill.
            SetStopLoss(entryName, CalculationMode.Price, stop, false);
            SetProfitTarget(entryName, CalculationMode.Price, target);

            plannedDirection = direction;
            plannedEntryPrice = plannedEntry;
            plannedStopPrice = stop;
            plannedQuantity = quantity;
            entryPending = true;

            // Reserve the daily entry slot before submission; no retry on
            // cancellation or error and never a third session entry.
            entriesToday++;
            if (entriesToday == 1)
                firstDirection = direction;
            if (State == State.Realtime)
                Diag("SUBMIT " + entryName + " qty=" + quantity
                    + " plannedEntry=" + plannedEntry.ToString("F2")
                    + " stop=" + stop.ToString("F2")
                    + " target=" + target.ToString("F2")
                    + " plannedRisk=" + (plannedLossPerContract * quantity).ToString("F2"),
                    true);

            if (direction == 1)
                EnterLong(quantity, entryName);
            else
                EnterShort(quantity, entryName);
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

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Max adverse entry slippage (ticks)", GroupName = "Live safeguards", Order = 7)]
        public int MaxAdverseEntrySlippageTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MNQ only when live", GroupName = "Live safeguards", Order = 8)]
        public bool MNQOnlyWhenLive { get; set; }
    }
}
