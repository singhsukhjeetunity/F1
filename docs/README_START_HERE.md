# JJ Simon Fair Pricing Theory — independent MT5 research EA v1.00

**Deliverables:** `JJ_Simon_FairPrice_Research_v1.mq5` (human-readable source for MetaEditor). This is NOT official JJ Simon code and does NOT reproduce his private discretion or funded-account routing. **It has not been compiled or backtested in MT5 in this environment.** The user must compile and validate it before relying on results.

## Why this first build is intentionally narrow

We want an evidence-based initial backtest with no post-hoc tweaks. This implements a mechanical *research interpretation* of the evaluation-side 9:30–11:00 Eastern setup described in JJ's interview and published playbooks. News setups and dynamic fair-price replacement are **not** included because they cannot be recreated from M1 OHLC alone without extra event data and precise discretionary rules.

### Source hierarchy

1. Interview supplied by user: https://youtu.be/KHEQ5g55dQ4 (continuation phase, 25-point / 38-point evaluation brackets, displacement, BOS, discretionary funded-model differences).
2. JJ's published TradeZella rules: https://www.tradezella.com/strategies/fair-pricing-theory-strategy (opposite-colour displacement, close beyond previous wick, BOS candle close, 3 reversion-loss cut-off, normal fair price from 9:30 open).
3. JJ-attributed FX Replay article: https://fxreplay-webflow.fxreplay.app/strategies/jj-simons-fair-value-theory-nq-strategy (alternative combined BOS + displacement and ATR-tier stops).

The sources differ. The EA exposes major variants as inputs. The default implements the TradeZella/interview *either/or* confirmation, not the stricter FX Replay interpretation.

## Exact default mechanics

* **Time**: read imported bar timestamps as UTC and convert to US Eastern, observing historical US daylight-saving changes since 2007.
* **Symbol**: use the CUSTOM NQ FUTURES symbol you created. Chart timeframe **M1** only. Require actual NQ (not NAS100 CFD) point prices, tick increment normally 0.25.
* **Fair reference**: the actual 9:30 Eastern 1-minute candle's OPEN. If that bar is missing, no entries that session.
* **Opening direction**: direction of completed 9:30 candle; a doji means no signals that session.
* **Continuation**: during first 5 minutes (9:30–9:34 candle times), in opening direction, when a completed qualifying displacement candle OR closed BOS is found. At most one continuation trade per session. By default the *opening candle itself is not traded* (`InpAllowOpeningBarSignal=false`): first possible signal is the completed 9:31 candle, entered on first tick of 9:32. This is a conservative interpretation of the 'opening candle alone is not enough' rule; toggle it to test entry after opening bar closes instead.
* **Reversion**: signals from completed 9:35 through 10:59 bar, trade toward 9:30 opening price only after a qualifying displacement OR BOS. Long when closed price < fair; short when closed price > fair. No entry after 11:00.
* **Displacement**: bullish candle replaces bearish predecessor, has strictly larger real body and closes > predecessor HIGH; bearish reverse. Candle must be complete. This uses published TradeZella rules.
* **BOS**: price CLOSE breaks most recent confirmed 1-bar-left/1-bar-right local pivot HIGH/LOW. Prior close must not already be through that pivot. The width and lookback are fixed *implementation proxies*, not precise private JJ rules. Both bars on either side of pivot must be already CLOSED before signal. Inputs let you inspect sensitivity.
* **Stop / target**: fixed price-distance 25 / 38 NQ index points, with exchange-tick rounding. No trailing, breakeven, scaling, or discretionary exits. Position remains until stop/target; 11:00 cutoff is **entries only**.
* **Sizing**: default 1 contract per trade, not per-account dynamic evaluation sizing; no portfolio / prop simulator, no coupling with E2.
* **Frequency and loss guard**: one position per symbol at a time; at most one continuation per session; stop reversion entries after 3 consecutive losing reversion positions in that session. A winner resets the consecutive count.
* **Afternoon**: 14:00–15:00 available but OFF by default. 8:30 news, Asia and London sessions intentionally excluded from v1.
* **Research-only**: `InpTesterOnly=true` blocks installation on non-Tester terminals by default.

## Known omissions and limitations (do not mislabel results as JJ's actual results)

* The qualitative 6–12h higher-timeframe bias is not given a precise algorithm by JJ. **No bias filter** is imposed in v1; this is a material omission.
* A significant move 'away from fair' is subjective. The v1 proxy uses bar location relative to session open but no extra excursion threshold.
* FX Replay uses a conjunction BOS+displacement and a `<20%` counter-wick interpretation, whereas TradeZella says displacement **or** BOS. The variant is selectable, not silently resolved.
* Mechanical BOS 'meaningful swing' definition is uncertain: `InpSwingWidth` and `InpBOSLookback` are assumptions, not verbatim rules.
* Dynamic fair-value shifts, sudden-news detection, and scheduled news calendar are not represented.
* Trade management differs for funded accounts; this file deliberately implements evaluation-style fixed exits only.
* Signal-side historical bars are OHLC. Without trade/quote ticks and the true bid/ask record, generated ticks cannot recover true stop-versus-target order within a minute, exact spreads or slippage.
* An NQ continuous contract can introduce rollover discontinuities, and OHLC source timezone is not yet independently validated. Until reconciled, treat returns as **exploratory**, not investable.
* No MT5 compiler or MetaTrader Strategy Tester is available in this environment; correctness of compiled binary and order execution remains unverified.

## Import and test in MetaTrader 5

1. Open **MetaEditor** in MT5 (F4). **File > Open** the `.mq5`, or copy it into `MQL5/Experts/JJ_Research/` and restart/reload Navigator. Hit **F7**. Resolve any MetaEditor diagnostics and do not proceed unless compile shows zero errors.
2. In MT5, create a **Custom Symbol** (View > Symbols > Create Custom Symbol, depending on build) named e.g. `NQ_FUTURES`. Import your 1-minute historical OHLC into its **Bars** history, with timestamps in UTC if you use default `InpDataTimeZone=JJ_DATA_UTC`.
3. Set symbol financial parameters **before testing**. For CME E-mini Nasdaq-100, the tick increment is **0.25 index points**, with **$5 USD per tick per NQ contract** ($20 per whole index point). Make sure the account/tester reports reflect these economics. Typically one lot is one contract; check actual custom symbol settings and `Journal` tick-value diagnostics. Also configure realistic commissions, spread and execution model.
4. **Strategy Tester:** select this EA; use `NQ_FUTURES`, `M1`, sample 2015–2024 with 2025 held for validation. Select realistic initial deposit and leverage/margin. Use **Every tick based on real ticks** ONLY if you actually have a tick dataset; otherwise use **Every tick** generated from imported M1 OHLC, and label results appropriately.
5. Run a short visual validation first (e.g. 2 weeks spanning the spring/fall DST change) and inspect 9:30 open, earliest continuation, 9:35 reversion boundary, BOS confirmation, protective orders, tick/point conversion, no look-ahead.
6. Run the main test with default inputs. Export MT5 **Results/Deals** and **Report**. Signal audit file `JJ_Simon_Signals_<magic>.csv` is written in the Strategy Tester's sandbox `MQL5/Files`, not necessarily your terminal's live Files folder.
7. Run a *separate* comparison with `InpConfirmation=JJ_DISPLACEMENT_AND_BOS` and optionally `InpStopModel=JJ_ATR_TIERS_1P5R`. Do not combine these into a single optimized backtest or choose based only on in-sample performance.
8. Review the data and results for wrong DST, unrealistic zero fees, phantom rollover gaps, intra-bar exit ambiguity, and contract sizes before any prop-firm modelling.

## Inputs relevant to the initial run

| Input | Initial value | Notes |
|---|---|---|
| `InpTesterOnly` | `true` | Blocks accidental live trading |
| `InpTradeNYMorning` | `true` | 9:30-11:00 US/Eastern |
| `InpTradeNYAfternoon` | `false` | Research later |
| `InpConfirmation` | `JJ_DISPLACEMENT_OR_BOS` | Based on interview + TradeZella |
| `InpAllowOpeningBarSignal` | `false` | Discretionary implementation choice |
| `InpSwingWidth` | `1` | Operational BOS proxy, not verified exact |
| `InpBOSLookback` | `30` | Operational BOS proxy, not verified exact |
| `InpContinuationMinutes` | `5` | Interview version vs FX Replay 10-15 |
| `InpStopModel` | `JJ_FIXED_25_38` | Evaluation bracket |
| `InpVolumeContracts` | `1` | Check tick value, margin and currency |
| `InpMaxRevConsecLosses` | `3` | Session-local reversion guard |
| `InpDataTimeZone` | `JJ_DATA_UTC` | Assumes CSV timestamps UTC |

## Go/no-go before multi-account work

The output of this stage is **not an equity-curve contest**. It is a chronological sample of trades that can feed multiple evaluation-account simulations later. Before allocating any of your US$1,000 bankroll, require: (a) price/time/provenance checks; (b) stable results under reasonable fee/slippage stress; (c) out-of-sample comparison, and (d) a real prop-firm rules simulation. Do NOT risk cash on results generated from an unaudited futures continuous series.