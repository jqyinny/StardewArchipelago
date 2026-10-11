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

Remote multiplayer (two separate machines, as opposed to split-screen) is now
being tested for real, and it surfaced an architecture gap that split-screen
had been masking:

**The core finding:** `VanillaCodeInjectionInitializer.Initialize` (and the
similar `CodeInjectionInitializer.Initialize`) only ever set static field
*references* on each check-detection class (e.g.
`CraftingInjections._locationChecker = locationChecker`) - they never call
`_harmony.Patch(...)`. The actual Harmony patch installation that makes the
game's own methods call into this mod's code at all happens in separate
patcher classes (`VanillaLocationPatcher`/`LocationPatcher`, `MailPatcher`,
`RandomizedLogicPatcher`, `ItemPatcher`), **all of which were only ever
constructed and applied inside the host-only `InitializeAfterConnection`.** A
farmhand's own process never installed any of these patches, so a farmhand
crafting/foraging/leveling/opening a mailed letter/walking through a
randomized entrance just ran unmodified vanilla code - no error, since
nothing was even hooking the method in that process to produce one.

Fixed by mirroring each of these patcher constructions in
`OnFarmhandSlotDataReceived`, with farmhand-local objects:

**Confirmed fixed by testing:**
- `LocationPatcher.ReplaceAllLocationsRewardsWithChecks()` - the ~80
  Craftsanity/Museumsanity/etc. check-detection patches. A farmhand
  successfully reported a check after this fix.

**Confirmed fixed by testing (more):**
- `MailPatcher.PatchMailBoxForApItems()`, `RandomizedLogicPatcher.PatchAllGameLogic()`
  (entrance randomization, shops, mines, community center, etc.),
  `ItemPatcher.PatchApItems()`, `JojapocalypseManager` (needed even with
  Jojapocalypse disabled - a different farmhand-mirrored class calls
  unconditionally into one of its Consequences classes, whose static fields
  are only ever set in this constructor), `EntranceManager.ResetCheckedEntrancesToday()`
  (a second, separate NRE blocking entrance randomization after the first fix).

**Still open, same shape, lower priority (not in current test scope):**
`GoalManager.InjectGoalMethods()`, `TileSanityManager.PatchWalk()`,
`ModRandomizedLogicPatcher` (mod content) are all the same host-only-patcher
pattern and likely have the same farmhand gap.

**A second, larger architectural gap:** `GetReceivedItemCount`/`HasReceivedItem`
(inherited, non-virtual) read from the live `ArchipelagoSession` - null on a
farmhand, so these silently return 0/false instead of the real answer (e.g.
a Progressive Mine Elevator release showed the new floor for the host but not
a farmhand). A repo-wide search found **160+ call sites across 50+ files**
using this pattern - skill levels, masteries, TV unlocks, movie theater,
shortcuts, bundles, recipes, weapons, buffs, and more. Rewriting all of them
isn't realistic in one pass, so this adds `StardewArchipelagoClient.
GetReceivedItemCountSafe`/`HasReceivedItemSafe` - backed by a local cache
seeded once from the host's complete `GetAllReceivedItemNamesAndCounts()`
(sent alongside `SlotData`/`State` in `SlotDataSnapshotMessage`) and kept
live via `RecordFarmhandReceivedItem` as new items arrive. Only the
confirmed case (`MineshaftInjections`' elevator checks) has been migrated to
the `*Safe` methods so far - everything else of the 160+ is still on the raw
methods and will misbehave for a farmhand until migrated incrementally.

**Not yet done:**
- `GiftSender` is reused as-is from the host's shared gift handler - gifting
  specifically from a farmhand will misbehave.
- A farmhand's already-checked-location state isn't synced back to it, so
  client-side UI that reflects checked locations (tooltips, walksanity
  overlays) may show stale info on a farmhand's screen.
- Message volume/reliability under heavy load (many checks firing in quick
  succession) is untested.

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

### Setting up a farmhand/second machine from GitHub

A farmhand needs this exact branch's build, not the stock released mod - the
stock mod has no `Context.IsMainPlayer` gating or `ModMessaging`, so it won't
understand anything the host sends and will try to open its own independent AP
connection instead of joining the host's. Two ways to get it onto a second
machine:

**Option A: Build from source on that machine.**
1. Install the prerequisites from Contributing.md's Software section (Stardew
   Valley, SMAPI, the .NET SDK, a git client - a full IDE isn't required, the
   `dotnet` CLI is enough).
2. Clone this fork at the `multiplayer-support` branch:
   ```
   git clone --branch multiplayer-support https://github.com/jqyinny/StardewArchipelago.git
   ```
3. Build it against that machine's own Stardew Valley install:
   ```
   dotnet build StardewArchipelago/StardewArchipelago/StardewArchipelago.csproj -c Debug -p:GamePath="<path to Stardew Valley>"
   ```
   MSBuild usually auto-detects the game path, so `-p:GamePath` is only needed
   if that fails (see Contributing.md's "Game Path" section for the
   `Directory.Build.props` alternative). This build step uses
   [ModBuildConfig](https://github.com/Pathoschild/SMAPI-ModBuildConfig), which
   packages and copies the mod straight into
   `<GamePath>/Mods/StardewArchipelago/` - no separate install/copy step.
4. Launch the game through SMAPI and confirm `KaitoKid.StardewArchipelago`
   loads without errors in the SMAPI console.

**Option B: Copy an already-built mod folder.** If someone else already built
this branch, copying their deployed `<GamePath>/Mods/StardewArchipelago/`
folder onto the second machine's `Mods/` folder works too - the mod is fully
self-contained (its own DLL, its dependency DLLs, and its content folders), so
there's no separate install step. Either way, make sure there isn't already a
different copy of the mod (e.g. the stock release) providing the same
`KaitoKid.StardewArchipelago` UniqueID somewhere else in `Mods/` - SMAPI will
only load one of them, and which one it picks isn't something to rely on.

The farmhand machine does **not** need an Archipelago account, server access,
or an Archipelago client of any kind - it never connects to Archipelago
directly, only to the host, over SMAPI's own multiplayer connection.

### Testing

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
