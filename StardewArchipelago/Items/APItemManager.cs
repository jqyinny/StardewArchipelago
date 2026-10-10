using System.Collections.Generic;
using HarmonyLib;
using KaitoKid.ArchipelagoUtilities.Net;
using KaitoKid.ArchipelagoUtilities.Net.Client;
using KaitoKid.Utilities.Interfaces;
using StardewArchipelago.Archipelago;
using StardewArchipelago.Items.Mail;
using StardewArchipelago.Items.Traps;
using StardewArchipelago.Networking;
using StardewArchipelago.Stardew;
using StardewModdingAPI;

namespace StardewArchipelago.Items
{
    public class APItemManager : ItemManager
    {
        private readonly ItemParser _itemParser;
        private readonly Mailman _mail;
        private readonly ModMessaging _modMessaging;

        public ItemParser ItemParser => _itemParser;
        public TrapManager TrapManager => _itemParser.TrapManager;

        public APItemManager(ILogger logger, IModHelper helper, Harmony harmony, StardewArchipelagoClient archipelago, LocationChecker locationChecker, StardewItemManager itemManager, Mailman mail, TrapExecutor trapExecutor, GiftTrapManager giftTrapManager, IEnumerable<ReceivedItem> itemsAlreadyProcessed, ModMessaging modMessaging) : base(archipelago, itemsAlreadyProcessed)
        {
            _itemParser = new ItemParser(logger, helper, harmony, archipelago, locationChecker, itemManager, trapExecutor, giftTrapManager);
            _mail = mail;
            _modMessaging = modMessaging;
        }

        protected override void ProcessItem(ReceivedItem receivedItem, bool immediatelyIfPossible)
        {
            // Every received item is applied to every connected player, not just the host (see
            // NOTES.md "item distribution"). The host still makes 100% of the decision about what
            // the item does (below, unchanged); farmhands just replay that same decision locally.
            _modMessaging.BroadcastItemReceived(receivedItem);

            if (immediatelyIfPossible)
            {
                if (_itemParser.TrySendItemImmediately(receivedItem))
                {
                    return;
                }
            }

            var attachment = _itemParser.ProcessItemAsLetter(receivedItem);
            attachment.SendToPlayer(_mail);
        }
    }
}
