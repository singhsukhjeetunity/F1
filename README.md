# F1 — NASDAQ futures systematic research (NinjaTrader 8)

Fresh research repository for independently implemented **NQ/MNQ** automated strategies. The previous MT5/MQL5 research files have been removed from the current `main` tree; historical commits remain in Git history.

## Contents

- `NinjaTrader/Indicators/NQSessionBreakout.cs` — original working chart indicator from the prior conversation, unchanged.
- `NinjaTrader/Strategies/NQSessionBreakoutStrategy.cs` — **research-only** executable NinjaScript strategy for Strategy Analyzer; this uses actual strategy positions and managed orders rather than the indicator's virtual position tracking.
- `docs/BACKTEST.md` — installation and baseline test procedure.
- `docs/SPEC.md` — rules, interpretation assumptions and material limitations.

## Fast start

1. Set **Tools → Options → General → Time zone** to **Eastern Time** in NinjaTrader.
2. Copy the indicator to `Documents/NinjaTrader 8/bin/Custom/Indicators/`.
3. Copy the strategy to `Documents/NinjaTrader 8/bin/Custom/Strategies/`.
4. In **New → NinjaScript Editor**, press **F5** to compile. The supplied indicator previously compiled on the user's machine; the newly authored strategy has **not** been compiled or executed on that machine.
5. Open **New → Strategy Analyzer** → **Backtest**, select `NQSessionBreakoutStrategy`, choose an **NQ/MNQ 5-minute** series with 09:30 New York data, and run the baseline test.

**Do not enable this research strategy on a funded or evaluation account yet.** Historical order-fills, session VWAP anchoring, real-time handling, broker rejection/reconnect behavior, slippage, risk limits and shutdown compliance require validation.

The video's reported results are unverified and are **not** performance claims for F1.


## Fixed-dollar risk sizing

`NQSessionBreakoutStrategy` calculates quantity from its 5-minute signal
candle's stop distance and the instrument's point value. Default
`FixedRiskDollars = 100`, `MaxContracts = 20`,
`CostReservePerContract = 0`. It rounds down to the nearest whole contract,
skips trades too large for the risk budget, and preserves the original
strategy/indicator separation. Details: `docs/BACKTEST.md`.

**Important:** This caps only *planned* stop risk, not actual market-executed
losses. It has not been compiled or live-tested in NinjaTrader. Do not deploy
to a prop evaluation until execution, daily loss and firm rule compliance
controls are validated.


## Safety-hardening update (2026-11-24)

The extra live-entry toggle and account-equity floor have been removed.
NinjaTrader's native strategy Enabled control now governs execution.
StrategySessionLossLimit remains available: 0 disables it; any positive
dollar setting enables the strategy-only loss guard. Existing order,
disconnection and duplicate-entry safeguards remain. See docs/SAFETY.md.

**Not a deployment-ready or firm-rule-certified strategy.** NinjaTrader
compilation and Sim101 runtime validation are required.
