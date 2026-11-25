# F1 code safety work — 2026-11-24

The existing strategy has a new **fail-closed real-time entry mode**. Its original
09:30 opening-range/VWAP signals, fixed planned dollar-risk position sizing,
0.5R target and maximum-two-trades logic are preserved. The indicator is separate
and unchanged; no strategy drawings were added.

## Added protections

- Trading is controlled by NinjaTrader's native strategy Enabled checkbox,
  without a separate internal live-order switch.
- MNQ-only live instrument restriction remains enabled by default.
- NinjaTrader built-in StopCancelClose (order rejections), StopStrategy
  (connection loss), zero automatic restart attempts and WaitUntilFlat.
- Logs account, startup state, New York session reset, fills, rejected and
  cancelled entry orders, and connection-status changes.
- Prevents a duplicate pending entry; reserves an entry slot before submitting.
- Refuses new entries when account instrument position is non-flat.
- Refuses new entries for the remaining New York session after a mid-session
  real-time startup; manual account reconciliation remains mandatory.
- Checks entry fills for large adverse slippage or filled-price planned-stop
  risk above budget, and requests an emergency close when exceeded.

## Important limitations — not deployment approval

1. None of this has been **compiled or runtime-tested in NinjaTrader**.
   Working C# text alone does not prove it is safe.
2. **No Lucid daily-loss or trailing drawdown enforcement in F1:** the
   strategy intentionally has no session loss-limit condition. The prop firm's
   own controls are separate and may not prevent slippage beyond a threshold.
3. Trade-level planned stop risk and the entry-fill deviation check are
   not account-level loss limits. Other positions or copied accounts are not
   covered.
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
9. The session-loss limit and its PnL-based entry/exit checks have been removed.
   Original strategy risk sizing and stop/target behavior remain; compare
   historical trade lists after installing this revision.

## Remaining runtime validation (not claimed complete)

- F5 compile on NinjaTrader 8, with only one class copy under Custom.
- Baseline parity on MNQ 5m with original historical settings and data.
- Sim101: native Enabled/Disabled, entry rejection, stop rejection,
  partial fills, slippage and entry-fill deviation safety.
- Sim101: disconnections, 09:40 restart, day rollover, orphaned positions
  and end-of-session behavior.
- Verify session timezone, holidays and shortened trading hours.
- Final forward-test on a simulation account before buying any evaluation.
- No live deployment or copying until all runtime checks pass.

NinjaTrader reference:
https://static.ninjatrader.com/support/helpGuides/nt8/realtimeerrorhandling.htm
https://static.ninjatrader.com/support/helpGuides/nt8/connectionlosshandling.htm
https://static.ninjatrader.com/support/helpGuides/nt8/onorderupdate.htm
https://static.ninjatrader.com/support/helpGuides/nt8/closestrategy.htm
