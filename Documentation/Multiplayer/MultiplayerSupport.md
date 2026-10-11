# Native Multiplayer Support (work in progress)

This branch adds support for Stardew Valley's native multiplayer (split-screen local
co-op and remote play over LAN/Steam/GOG) on top of the existing single-player
Archipelago integration. Only the host connects to the Archipelago server; farmhands
get everything relayed to them over SMAPI's own mod-messaging channel.

## Design

- **Host-authoritative.** Only the host (`Context.IsMainPlayer`) ever holds a real
  Archipelago connection. A farmhand never talks to the AP server directly.
- **`ModMessaging`** (`StardewArchipelago/Networking/ModMessaging.cs`) is the
  host ↔ farmhand channel, built on `IMultiplayerHelper.SendMessage` /
  `ModMessageReceived`. It has no replay/queueing of its own, so the host resends
  its latest snapshot to a peer specifically when they connect
  (`OnPeerConnected`) - this is also the only way local co-op actually works,
  since the host is always already connected before a farmhand can join.
- **`SlotData` and `State` sync.** A farmhand seeds its own (never-connected)
  `StardewArchipelagoClient` from the host's raw slot-data fields
  (`StardewArchipelagoClient.SeedSlotDataFromSnapshot`) and its own
  `ArchipelagoStateDto` from a snapshot of the host's `State`
  (`ArchipelagoStateDto.ForFarmhandSync`, which strips the AP room password -
  a farmhand never needs it). Both travel in one message
  (`SlotDataSnapshotMessage`) to avoid any ordering race between them.
- **Item/trap fan-out.** The host decides the effect exactly as it already does
  in single-player (`APItemManager.ProcessItem`), broadcasts it
  (`ModMessaging.BroadcastItemReceived`), and every connected player - including
  the host itself - applies it locally. There's no "pick a recipient" logic
  anywhere: every item and trap applies to every connected player.
- **Location-check reporting.** `CoopLocationChecker` (a `StardewLocationChecker`
  subclass) branches on `Context.IsMainPlayer` at call time: on the host it
  behaves exactly like before, and on a farmhand it forwards the check to the
  host over `ModMessaging` instead. Every check type that depends on the
  abstract `LocationChecker` base type gets this for free - no changes needed
  per check type.

## Current status

**Working (validated via split-screen testing):**
- Host-only AP connection, `SlotData`/`State` sync, item/trap fan-out.
- Farmhand location checks for everything wired through
  `VanillaCodeInjectionInitializer.Initialize` (~80 check types: Craftsanity,
  Museumsanity, Monstersanity, Booksanity, Fishsanity, Friendsanity, Shipsanity,
  Walnutsanity, and most of `Locations/CodeInjections/Vanilla/`).

**Not yet done:**
- ~30-35 check types in `GameModifications/CodeInjections/` (a separate,
  more "world configuration"-flavored initializer) are still host-only.
- `GiftSender` is reused as-is from the host's shared gift handler - gifting
  specifically from a farmhand will misbehave.
- World-config sync (shop stock, randomized data, entrance warps, building
  costs): the sync mechanism exists, but the patchers that would need it
  haven't been updated to read from a farmhand-local source.
- Remote multiplayer (as opposed to split-screen) is only lightly tested so
  far - the mod behavior should be identical since `SendMessage` is documented
  to work the same way regardless of transport, but this hasn't been verified
  across a real network boundary for every feature above.

**Known latent risks, not yet causing observed bugs:**
- `TrapExecutor`/`TrapManager` have a few `static` fields that get silently
  overwritten when a farmhand's separate instance is constructed. Hasn't broken
  anything observed so far (the trap effects that have been tested read
  `Game1.player` directly, not the statics), but could affect other effects.
- A farmhand's already-checked-location state isn't synced back to it, so
  client-side UI that reflects checked locations (tooltips, walksanity
  overlays) may show stale info on a farmhand's screen.
- Message volume/reliability under heavy load (many checks firing in quick
  succession) is untested.

## Building and testing

Build like any other change to the mod (see
[Contributing.md](../Contributing/Contributing.md)) - this branch doesn't add
any new build steps or dependencies.

To exercise the multiplayer-specific code paths you need a second player, either:

- **Split-screen (same machine):** create/load the save normally - the mod's
  usual AP connection screen appears exactly as in single-player - then from
  the pause menu go to Options → Multiplayer → "Start Local Co-op" and connect
  a second controller/profile.
- **Remote (two machines):** create/load the save normally on the host machine
  so the mod connects to AP as usual, save and return to the title screen, then
  reopen that same save via **"Co-op"** instead of "Load" (not "Host New
  Farm" - that's a separate creation flow that the mod's connection UI isn't
  hooked into). The farmhand machine needs the mod installed (so it can apply
  received items and report its own checks) but never connects to Archipelago
  itself - no AP account, server access, or client needed on that machine.
  Remote players then join via Steam invite, an invite code (GOG Galaxy/Steam),
  or direct IP (UDP port 24642 by default).

In both cases, the host must remain connected and the save open for farmhands
to be able to join or stay in the world.
