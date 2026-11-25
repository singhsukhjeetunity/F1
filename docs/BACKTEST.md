# Backtest checklist — NinjaTrader 8

1. Ensure NinjaTrader timezone is **Eastern/New York**, and select a trading-hours template containing the NY 09:30 opening candle. A chart that suppresses NY session hours cannot reproduce the stated rules.
2. Place source files in the `Custom/Indicators` and `Custom/Strategies` folders, respectively, and press **F5** in NinjaScript Editor.
3. In **Control Center → New → Strategy Analyzer → Backtest**, select `NQSessionBreakoutStrategy`. Select an **NQ or MNQ** contract and **5 Minute** bars; set start/end dates within your downloaded data.
4. Start with **1 contract**, `LastEntryTime = 153000`, `FlattenTime = 155500`, `MaxStopPoints = 0` (disabled). Do not optimize before obtaining the unmodified baseline.
5. Turn on commission inclusion and choose a realistic NQ/MNQ commission schedule. Set a nonzero slippage assumption. Use **High** order fill resolution with **Tick, 1** if supported and actual tick data is available.
6. Inspect **Trades**, **Executions**, **Analysis**, **Drawdown** and the **equity curve**. Confirm max two entries/day, second only opposite direction, and exits tied to stop/target prices. Export trades for independent analysis.
7. Compare a few dates to the `NQSessionBreakout` chart indicator. Differences are expected because of virtual vs actual fills and fill timing.
8. Stress-test with higher friction, rolling futures contract changes and out-of-sample periods. Do not accept the YouTube creator's advertised returns as verified.

## Important operational warning

This initial strategy is **for Strategy Analyzer / simulator research only**. NinjaScript code can submit real orders when enabled on a live account. Do **not** enable it on Lucid or any funded account until real-time order-state, account restrictions and prop-firm compliance controls are finished and independently tested.

Official NinjaTrader references:
- https://ninjatrader.com/support/helpguides/nt8/setstoploss.htm
- https://ninjatrader.com/support/helpguides/nt8/setprofittarget.htm
- https://static.ninjatrader.com/support/helpGuides/nt8/understanding_historical_fill_.htm


## Fixed-dollar risk position sizing (2026-11-24 update)

The strategy now sizes each signal dynamically rather than using a fixed
`Contracts` input. `FixedRiskDollars` defaults to **$100 planned stop risk per
trade**, `MaxContracts` defaults to **20**, and `CostReservePerContract`
defaults to **$0** (set this to your estimated round-trip friction allowance
in addition to selecting commissions/slippage in Strategy Analyzer).

For a long signal, stop distance = signal close - signal candle low.
For a short signal, stop distance = signal candle high - signal close.
Dollar risk per contract = distance in points ×
`Instrument.MasterInstrument.PointValue` + `CostReservePerContract`.
The strategy enters `floor(FixedRiskDollars / riskPerContract)`, capped by
`MaxContracts`. It **skips** an unaffordable trade (zero contracts), and
never rounds upward to force a fill. Whole futures contracts mean actual planned
risk is often LESS than the dollar budget.

Example: MNQ ($2/point), signal stop 25 points, fixed budget $150,
cost reserve $0 → planned stop risk $50 per contract → **3 MNQ**.
For a 90-point stop on MNQ at $100 risk, it skips (1 contract would risk $180).

**Risk caveat:** This does NOT guarantee a $100 realized loss. The original
strategy sends a market entry at next-bar fill, while stop/target are priced
from the prior signal close. Gaps, stop slippage, fees, commissions and fills
can exceed the budget. For the intended 25K LucidFlex use case,
`MaxContracts` must never exceed the current permitted contract limit, and
additional account-level loss guards are required before live use.

The opening range, VWAP filter, 0.5R target, opposite-direction second trade,
session parameters and absence of chart drawing were preserved.

## Export trades for Edge Engine

After a complete MNQ backtest, select **Trades** in the Strategy Analyzer
**Display** dropdown. Right-click inside the trades grid → **Export** → **CSV**.
Save the full-history trade list and retain fields including entry/exit
timestamps, quantity and realized P&L. Export the nonoverlapping full-period
test, not multiple overlapping screenshots. Confirm the CSV's dates cover
the requested period.


## Live safeguards (2026-11-24)

For historical research, leave EnableLiveOrders false, StrategySessionLossLimit
zero and MinimumAccountNetLiquidation zero. This avoids mixing the
live-only risk gate with Edge Engine research assumptions. For simulated
execution and eventual live-account configuration, read docs/SAFETY.md.
