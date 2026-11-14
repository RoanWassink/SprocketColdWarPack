#if CAMPAIGN
using SprocketExpedition.Core;
using SprocketExpedition.Core.Presentation;
using SprocketExpedition.Core.Run;
using SprocketUiKit;
using UnityEngine;

namespace SprocketExpedition.Game;

internal static partial class CampaignNativeMenu
{
    private static bool armoryManageStored;
    private static bool armoryManageActive;
    // Rarity colours: Kit card accent and chip colour, next to the rarity text.
    private static Color RewardColor(RewardRarity rarity) => rarity switch
    {
        RewardRarity.Special => new Color(1f, .81f, .13f, 1),
        RewardRarity.Rare => new Color(.87f, .22f, .20f, 1),
        RewardRarity.UltraRare => new Color(.68f, .34f, .92f, 1),
        _ => new Color(.25f, .60f, .98f, 1)
    };

    private static void ComposeArmory(MenuPage next, ArmoryView view)
    {
        void Run(Action action)
        { if (!CampaignControllerHost.Busy && ReferenceEquals(view, CampaignControllerHost.View)) action(); }
        var enabled = !CampaignControllerHost.Busy;
        if (view.Recovery)
        {
            next.Title = "EMERGENCY RECOVERY";
            next.Lines.Add("Your force was defeated. Use your active recovery charge to return to the editor and retry the same encounter.");
            next.Lines.Add("Enemy forces reset. Other item timers and the reward stay unchanged.");
            next.Actions.Add(new MenuButton("USE RECOVERY · REFIT & RETRY",
                () => Run(() => CampaignControllerHost.UseRecovery()), true, enabled));
            next.Actions.Add(new MenuButton("ACCEPT DEFEAT",
                () => Run(() => CampaignControllerHost.UseRecovery(true)), false, enabled));
            return;
        }
        var rewards = view.Rewards;
        var choosing = rewards.Choices.Count > 0;
        if (choosing) armoryManageActive = false;
        var managing = choosing && armoryManageStored;
        var managingActive = !choosing && armoryManageActive;
        next.Title = managingActive ? "ACTIVE ITEMS" : managing ? "MAKE ROOM FOR YOUR REWARD" : choosing ? "CHOOSE YOUR REWARD" : "ITEM INVENTORY";
        if (managing)
        {
            // Kit sub-page; back = BACK TO REWARD CHOICE
            next.SubPage = PageManage;
            next.Back = () => Run(() => { armoryManageStored = false; Build(); });
        }
        else if (managingActive)
        {
            // Kit sub-page; back = BACK TO STORED ITEMS
            next.SubPage = PageManage;
            next.Back = () => Run(() => { armoryManageActive = false; Build(); });
        }
        next.Lines.Add($"Round {view.Round} · fleet budget {view.Budget.Value:N0} · earned funds +{rewards.EarnedFunds:N0} · inventory {rewards.Inventory.Count}/6 · active {rewards.Active.Count}/3");
        if (rewards.Active.Count == 0) next.Lines.Add("No active items.");
        else foreach (var item in rewards.Active) next.Lines.Add("ACTIVE · " + RewardRules.Describe(item, true));
        var notice = view.Notice ?? (choosing && rewards.Inventory.Count >= 6
            ? "Inventory full: choose a stored item to discard, or decline this reward."
            : !choosing && rewards.Active.Count >= 3 ? "Active slots full: same-type replacement is allowed. Manage active items to discard a bonus and free a slot." : null);
        if (notice != null)
        {
            next.Status = notice;
            next.StatusKind = StatusKind.Warning;
        }
        var items = managingActive ? rewards.Active : choosing && !managing ? rewards.Choices : rewards.Inventory;
        List<MenuCard>? row = null;
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var description = RewardRules.Describe(item);
            var replacing = RewardRules.ActiveReplacement(rewards, item) != null;
            var blocked = RewardRules.ActivationBlockReason(rewards, item);
            if (item.BlueprintId != null && CampaignControllerHost.UsesNativeContent)
                description += "\nTank: " + CampaignControllerHost.DescribeEnemy(item.BlueprintId, "");
            if (managingActive) description = RewardRules.Describe(item, true) + "\nDiscarding ends this bonus without returning it to storage.";
            else if (replacing) description += "\nActivating replaces the active item and starts a fresh duration.";
            else if (!choosing && blocked != null) description += "\nAll active slots are occupied. Manage active items below.";
            var card = new MenuCard
            {
                Title = System.Text.RegularExpressions.Regex.Replace(item.Kind.ToString(), "(?<!^)([A-Z])", " $1"),
                Body = description,
                Tag = RarityText(item.Rarity),
                Colour = RewardColor(item.Rarity)
            };
            if (managingActive)
                card.Buttons.Add(new MenuButton("DISCARD ACTIVE ITEM",
                    () => Run(() => CampaignControllerHost.ItemAction("discard-active", item.Id)), false, enabled));
            else if (managing)
                card.Buttons.Add(new MenuButton("DISCARD THIS STORED ITEM",
                    () => Run(() => CampaignControllerHost.ItemAction("discard", item.Id)), false, enabled));
            else if (choosing)
                card.Buttons.Add(new MenuButton(rewards.Inventory.Count >= 6 ? "INVENTORY FULL" : "KEEP ITEM",
                    () => Run(() => CampaignControllerHost.ItemAction("claim", item.Id)), true,
                    enabled && rewards.Inventory.Count < 6));
            else
            {
                card.Buttons.Add(new MenuButton(item.Kind == RewardKind.OperationalFreedom ? "REROLL OFFERS" : replacing ? "REPLACE ACTIVE" : blocked != null ? "ACTIVE SLOTS FULL" : "ACTIVATE",
                    () => Run(() => CampaignControllerHost.ItemAction("activate", item.Id)), true, enabled && blocked == null));
                card.Buttons.Add(new MenuButton(item.Kind == RewardKind.WarBonds ? "CASH +15% NOW" : "DISCARD",
                    () => Run(() => CampaignControllerHost.ItemAction(item.Kind == RewardKind.WarBonds ? "cash" : "discard", item.Id)), false, enabled));
            }
            if (index % 3 == 0) next.CardRows.Add(row = new List<MenuCard>());
            row!.Add(card);
        }
        if (items.Count == 0)
        {
            if (managingActive) next.Notes.Add("No active items. Return to stored items to activate a bonus.");
            else
            {
                next.Notes.Add("Win encounters to find items. Blue: 1–2 victories · Red: 3–4 · Purple: 5–6.");
                next.Notes.Add("Stored items do not expire. Activate before choosing your next challenge.");
            }
        }
        if (choosing)
        {
            if (managing)
                next.Actions.Add(new MenuButton("BACK TO REWARD CHOICE",
                    () => Run(() => { armoryManageStored = false; Build(); }), true, enabled));
            else
            {
                next.Actions.Add(new MenuButton("DECLINE REWARD",
                    () => Run(() => CampaignControllerHost.ItemAction("skip")), false, enabled));
                if (rewards.Inventory.Count >= 6)
                    next.Actions.Add(new MenuButton("CHOOSE A STORED ITEM TO DISCARD",
                        () => Run(() => { armoryManageStored = true; Build(); }), false, enabled));
            }
        }
        else
        {
            next.Actions.Add(new MenuButton(CampaignControllerHost.ItemsFromDesigner ? "BACK TO BUILDING" : "BACK TO CHALLENGES",
                () => Run(CampaignControllerHost.LeaveItems), true, enabled));
            next.Actions.Add(new MenuButton(managingActive ? "BACK TO STORED ITEMS" : "MANAGE ACTIVE ITEMS",
                () => Run(() => { armoryManageActive = !armoryManageActive; Build(); }), false, enabled));
        }
    }
}
#endif
