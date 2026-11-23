# F1 NinjaTrader 8 — backtest and chart visuals

1. Set **Tools → Options → General → Time zone** to **Eastern / New York**. Ensure your NQ/MNQ 5-minute data series includes 09:30–09:35 ET and the correct RTH trading-hour segment.
2. Copy **only** `NQSessionBreakoutStrategy.cs` to `Documents/NinjaTrader 8/bin/Custom/Strategies/`. **Replace** its previous version; do not create a second file with the same class anywhere in `Custom`.
3. If you already have `NQSessionBreakout` in `Custom/Indicators`, leave that existing indicator intact. Do not copy the repository's indicator into another nested strategy folder.
4. Reopen NinjaTrader; press **F5** in **New → NinjaScript Editor**. Address any compiler errors before running.
5. Open **New → Strategy Analyzer → Backtest**, select `NQSessionBreakoutStrategy`, 5-minute NQ or MNQ, a date range with historical data, and `Show chart visuals = true`. Click **Run** and inspect the **Chart** tab. You can also apply the strategy to a chart for visual inspection with live execution **disabled**.
6. Plot legend:
   - **Blue**: 09:30 opening range high.
   - **Purple**: 09:30 opening range low.
   - **Gold**: five-minute HLC3-volume approximate session VWAP.
   - **Green up / red down arrows**: confirmed long/short signal at close, with labels.
   - **Steel blue horizontal line**: *planned* entry at confirmation candle close.
   - **Red horizontal line**: planned stop at breakout candle extreme.
   - **Green horizontal line**: planned 0.5R target.
   - **Deep sky blue dot**: actual historical/simulated entry fill (may differ from signal close).
   - **Green/red/orange dot**: actual execution exit profit/loss/flat, at execution price.
7. Trading rules are unchanged by plotting: strict 5-minute range cross with session VWAP filter, maximum two entries per day, only opposite-side second entry after full exit.
8. For a defensible backtest, include commission, fees, meaningful slippage and high-resolution tick fills where available. Review date/session behavior and day-boundary exits. Do not assume chart draws themselves prove execution or profit.

**Research only.** The indicator simulates exits using OHLC; the strategy uses actual simulated historical strategy executions, so signals/drawings can diverge. The visual-only update is not a live-trading compliance or execution-hardening release.
