# Active reward replacement — 24 October 2026

The reported "An active reward of kind 'CapturedPrototype' is already present"
was the explicit same-family activation rejection. Activation now replaces the
existing active family in place. A new type still needs a free active slot.

- The new pickup supplies its own magnitude, blueprint, rarity and full duration.
  The old item is consumed, its strength and remaining time are not added.
- Replacement works with all three active slots occupied and consumes only the
  selected inventory item. It also covers Lend Lease/Captured Prototype and Field
  Reinforcements/Emergency Reserve in either direction.
- Stored items do not tick. Activation starts the duration; each distinct victory
  consumes one round. Boss Captured Prototype lasts six victories from activation.
  Defeat, refit, technical retry and duplicated outcomes retain existing timing.
- War Bonds retain their special two-victory investment timer; replacing an
  investment discards its unpaid payout and starts a new investment. Cashing
  bonds and immediate offer rerolls retain their existing rules. Emergency Recovery
  still permits one owned charge.
- The native inventory shows REPLACE ACTIVE where appropriate, an explicit
  replacement/fresh-duration explanation, and remaining durations in victories.
  Claiming a reward into storage does not itself replace an active item.

No reward schema or saved slot data was changed. Existing valid checkpoints load
through the same path. Tests cover replacement with full slots, both support
aliases, new tank identity, weaker new magnitude, fresh timer, one free support,
victory/replay timing, duplicate activation rejection without mutation, revision,
restore and disk save/reload. Adventure 16/16, Run 30/30, Persistence 30/30 and
Integration scenarios pass. Campaign and proof builds pass with zero warnings or
errors. Native presentation and gameplay remain user-tested.

Installed with Sprocket closed and backups in
`artifacts/install-campaign-20261024-184241/journal.json`:

- Game SHA256: `89483D4C028D11E3510AF2CF9A52C2BBE560AA925BC7E625127127D2FB7AAA14`
- Core SHA256: `4C01CB3EE81B4536CD3986B12CC1DA67E74224EE0E9000DDA0627F3C54C3CD14`

Config bytes preserved; root ACL unchanged; executable retains inherited Medium
integrity. Build/check logs are `artifacts/reward-replace-*.txt`. Prior private boss
tracking remains included. No game process was launched or terminated.

User check: resume the run, activate the newly captured prototype using REPLACE
ACTIVE and confirm one active support item remains, with the new tank and six
victories. After one victory it should show five remaining; save/resume should
retain the new tank and timer.
