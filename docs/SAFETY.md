# F1 code safety work — 2026-11-24

The existing strategy has a new **fail-closed real-time entry mode**. Its original
09:30 opening-range/VWAP signals, fixed planned dollar-risk position sizing,
0.5R target and maximum-two-trades logic are preserved. The indicator is separate
and unchanged; no strategy drawings were added.

## Added protections

- Live entries OFF by default, including on Sim101 until manually armed.
- Arming requires a positive strategy session loss limit and account net
  liquidation floor. Default instrument restriction MNQ.
- NinjaTrader built-in StopCancelClose (order rejections), StopStrategy
  (connection loss), zero automatic restart attempts and WaitUntilFlat.
- Logs account, startup state, New York session reset, fills, rejected and
  cancelled entry orders, and connection-status changes.
- Prevents a duplicate pending entry; reserves an entry slot before submitting.
- Refuses new entries when account instrument position is non-flat.
- Refuses new entries for the remaining New York session after a mid-session
  real-time startup; manual account reconciliation remains mandatory.
- Checks strategy-only session PnL at bar close and, when realtime, on last
  market-data updates. A breach requests CloseStrategy for **this strategy**.
- Checks account NetLiquidation against a configurable static absolute floor;
  a breach requests CloseStrategy.
- Checks entry fills for large adverse slippage or filled-price planned-stop
  risk above budget, and requests an emergency close when exceeded.

## Important limitations — not deployment approval

1. None of this has been **compiled or runtime-tested in NinjaTrader**.
   Working C# text alone does not prove it is safe.
2. **No complete Lucid trailing drawdown implementation:** an absolute net
   liquidation floor is not a dynamically updated firm trailing-loss threshold.
   Provider account fields may be missing, stale or nonrepresentative.
3. The internal loss guard is only for this *strategy*, not other positions,
   copied accounts or the prop firm's consolidated risk rules.
4. A lost broker connection can prevent emergency orders from reaching the
   market. Restarts may leave working orders or positions; manual verification
   required. Error logs are not a substitute for brokerage risk controls.
5. VWAP still uses five-minute volume-weighted HLC3, not tick-accurate VWAP.
6. OnBarClose entries fill on a subsequent bar. Stops/targets reference signal
   close, not actual entry fill. Slippage and gaps may exceed fixed risk.
7. At 15:55 ET the strategy sends its existing market exit on the completed
   five-minute bar, generally filling afterward. Prop-mandated flat timing is
   **not guaranteed** by this mechanism.
8. The source has not been tested against actual Lucid account limits,
   sessions, rules, payout requirements, or third-party trade copying.
9. All new safeguards are dormant for historical backtests by default (loss
   limit = 0). Historical comparison still requires comparing trade lists.

## Remaining runtime validation (not claimed complete)

- F5 compile on NinjaTrader 8, with only one class copy under Custom.
- Baseline parity on MNQ 5m with original historical settings and data.
- Sim101: arming off/on, entry rejection, stop rejection, partial fills,
  slippage, session loss threshold, account floor threshold.
- Sim101: disconnections, 09:40 restart, day rollover, orphaned positions,
  missing account NetLiquidation values and end-of-session behavior.
- Verify session timezone, holidays and shortened trading hours.
- Final forward-test on a simulation account before buying any evaluation.
- No live deployment or copying until all runtime checks pass.

NinjaTrader reference:
https://static.ninjatrader.com/support/helpGuides/nt8/realtimeerrorhandling.htm
https://static.ninjatrader.com/support/helpGuides/nt8/connectionlosshandling.htm
https://static.ninjatrader.com/support/helpGuides/nt8/onorderupdate.htm
https://static.ninjatrader.com/support/helpGuides/nt8/closestrategy.htm
