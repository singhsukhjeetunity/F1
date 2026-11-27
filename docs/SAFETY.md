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


## Connection-lock fix and live signal diagnostics (2026-11-27)

The earlier strategy latched `liveFault=true` after **any** connection-status
change, with no recovery path even for a brief transition. This update separates
`liveConnectionReady` from `liveFault`. During a disconnected/connecting
event it temporarily blocks new entries; on a subsequent event with both order
and price statuses `Connected`, it resumes eligibility. A real order
rejection/cancel still latches a separate manual-reconciliation lock.
NinjaTrader's own `ConnectionLossHandling.StopStrategy` remains active for a
sustained loss, and auto-restart attempts stay at zero. A platform-stopped
strategy does not re-enable itself following recovery.

**Diagnostic output (realtime only):**
- Prints to NinjaScript **Output 1** on startup, 09:35 ET opening-range reset,
  and each 5-minute completed candle from 09:35 through 10:30 ET.
- Logs the bar time/close, previous close, opening high/low, approximate VWAP,
  raw breakout conditions, live connection status, startup block, fault, account
  flatness, pending entry and daily entry count.
- Logs later qualifying breakout bars and signals blocked by live gates.
- Logs when the chosen fixed-dollar risk is too low for **one MNQ contract**,
  including required dollars per contract.
- Logs strategy order submissions and fills, including planned risk.
- Sends low-frequency **startup/session/connection changes, blocked breakout
  signals, unaffordable signals and submitted orders** to Control Center Log
  as `NinjaScript` events as well as the Output window. This makes the
  diagnosis recoverable even when Output isn't open.

The opening-range logic, 5m VWAP approximation, 0.5R stop/target rules,
two-trades-per-day logic, per-trade planned risk sizing, and separate indicator
remain unchanged. No additional entry-eligibility conditions were introduced.

**For Monday 2026-11-30:** install the latest strategy, compile with F5,
open Output 1, connect market data, enable the strategy on Sim101 **before
09:30 ET** and keep NinjaTrader running. Check the Control Center Log for
`[F1]` diagnostics if Output 1 is blank. Confirm there is an actual complete
5-minute 09:35 bar. If a breakout appears but no order is sent, copy the
`BAR`, `BLOCKED` or `SUBMIT` log lines. Don't change live safeguards
or use a paid evaluation to diagnose.
