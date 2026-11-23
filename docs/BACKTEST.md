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
