# NQ Session Breakout — research specification

Source: user-provided transcript of https://youtu.be/EBF2seUrWHI (independent implementation, no creator code).

## Rules explicitly described

1. **NQ**, **5-minute** candles, New York cash-session open.
2. Fix the high/low of the **09:30–09:35 AM Eastern** bar.
3. Long only after a subsequent five-minute **close strictly above** that high and **above session VWAP**.
4. Short only after a subsequent five-minute **close strictly below** that low and **below session VWAP**.
5. Entry is described as at the confirming candle close; in NinjaTrader OnBarClose historical execution, a market order fills on the **next available bar**, which is necessarily different.
6. Stop at the opposite side of the **confirming breakout candle**: its low for longs, high for shorts.
7. Profit target = **0.5 x initial signal-bar stop distance**.
8. At most **two trades per NY session**. A second may occur only **after the first fully exits**, and only on a **fresh close through the opposite opening-range level**, regardless of first-trade outcome.

## Choices not specified by the video

- **VWAP anchoring/calculation**: the implementation starts accumulating at the NY 09:30 opening bar and uses `sum(volume * HLC3) / sum(volume)` on five-minute bars. This is an **approximation**, not tick/transaction VWAP. Difference may change signals and reported results.
- **Last entry**: default 15:30 ET, configurable; absent from the transcript.
- **Flattening**: default 15:55 ET signal for NY cash session, configurable. OnBarClose market exits ordinarily fill on the **next bar**; do not assume flat at exactly the configured timestamp.
- **Stops/targets after gap**: strategy brackets use the confirmation-bar **close**, NOT actual next-bar execution price. Executed reward/risk ratio may differ from 0.5R.
- **Opening range availability**: historical data/trading-hours template must contain a complete 09:30–09:35 bar. Early closes, session breaks, exchange holidays, and non-Eastern display time require specific testing.
- **Trade count**: incremented when signal order is submitted, not only when execution is confirmed. The prototype must be hardened for rejected orders/reconnections before live use.
- **Same-bar exit and opposite signal**: conservative gate requires later completed bar following exit.

## Verification gates before any VPS deployment

- Compile in NinjaTrader 8 and fix any compiler/runtime errors.
- Compare several indicator/strategy signal dates manually on **identical chart and data series**.
- Acquire actual 1-tick NQ/MNQ history; use Strategy Analyzer **High** order fill resolution / Tick where compatible.
- Add realistic commissions, exchange fees, and slippage.
- Compare monthly/annual out-of-sample results and drawdown measured against **actual prop-firm loss limits**, not nominal account size.
- Implement robust exchange/account timezone handling, tick VWAP, live position reconciliation, disconnect/reject recovery, time-flatten enforcement, and account-level risk guard.
- Forward test on Sim101 or equivalent before placing live/evaluation orders.

The chart indicator's stop/target hits are **virtual OHLC estimates**, and may disagree with historical or live strategy fills.
