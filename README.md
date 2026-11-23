# F1 — NASDAQ futures systematic research (NinjaTrader 8)

Independent NQ/MNQ futures research project. The previous MQL5 contents are gone from the current `main` tree (recoverable in Git history).

## Files

- `NinjaTrader/Indicators/NQSessionBreakout.cs` — standalone chart indicator (unchanged).
- `NinjaTrader/Strategies/NQSessionBreakoutStrategy.cs` — standalone executable Strategy Analyzer strategy **with its own chart visuals**. No indicator dependency.
- `docs/BACKTEST.md` — installation, plotting and backtesting instructions.
- `docs/SPEC.md` — system specification, assumptions, risks.

## Quick start

1. Set NinjaTrader's app/chart time zone to **Eastern Time**.
2. Copy the strategy file **only** to `Documents/NinjaTrader 8/bin/Custom/Strategies/`.
3. In **New → NinjaScript Editor**, press **F5** to compile.
4. Run `NQSessionBreakoutStrategy` on **NQ/MNQ 5-minute** bars (historical data must include 09:30–09:35 ET).
5. Open the Strategy Analyzer **Chart** tab, or apply the strategy to a chart in **disabled/simulation** mode, and set `Show chart visuals = true`.

The strategy renders 09:30 opening high/low, RTH-anchored approximate VWAP, signal arrows and labels, planned entry/stop/0.5R target lines, and actual execution markers (blue entry dot, green/red/orange exit dot).

**Do not install an additional copy of `NQSessionBreakout.cs` if it already exists in `Custom/Indicators/`**; duplicate class definitions block NinjaTrader compilation.

**Research only.** This strategy has not been compiled in the user's NinjaTrader after the new visualization update, nor validated for live/VPS/prop firm operation. Historical VWAP is approximate and actual market fills can differ from chart signal prices. The video's reported performance is unverified.
