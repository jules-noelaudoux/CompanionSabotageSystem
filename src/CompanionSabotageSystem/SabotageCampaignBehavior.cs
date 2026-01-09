using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using System.Collections.Generic;

namespace CompanionSabotageSystem
{
    public class SabotageCampaignBehavior : CampaignBehaviorBase
    {
        private SpyManager _spyManager = new SpyManager();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_spyManager", ref _spyManager);
            if (dataStore.IsLoading && _spyManager == null)
            {
                _spyManager = new SpyManager();
            }
        }

        private void OnDailyTick()
        {
            _spyManager.DailyTick();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            bool CanSendSpy(MenuCallbackArgs args)
            {
                args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                if (Settlement.CurrentSettlement == null || Settlement.CurrentSettlement.IsVillage) return false;
                if (Settlement.CurrentSettlement.OwnerClan == Clan.PlayerClan || Settlement.CurrentSettlement.IsUnderSiege) return false;
                return true;
            }

            void OpenSpyMenu(MenuCallbackArgs args)
            {
                OpenSpySelectionList();
            }

            // ID: G7k3n9Lp (Send an Agent)
            starter.AddGameMenuOption("town", "sabotage_mission_town", "{=G7k3n9Lp}Send an Agent",
                CanSendSpy, OpenSpyMenu, false, 2);

            starter.AddGameMenuOption("castle", "sabotage_mission_castle", "{=G7k3n9Lp}Send an Agent",
                CanSendSpy, OpenSpyMenu, false, 2);
        }

        private void OpenSpySelectionList()
        {
            List<InquiryElement> spies = new List<InquiryElement>();

            foreach (var troop in MobileParty.MainParty.MemberRoster.GetTroopRoster())
            {
                if (troop.Character.IsHero && !troop.Character.IsPlayerCharacter)
                {
                    Hero h = troop.Character.HeroObject;
                    if (_spyManager.IsHeroBusy(h)) continue;

                    if (h.GetSkillValue(DefaultSkills.Roguery) >= 30 && h.HitPoints > 40)
                    {
                        // ID: R8t5y2U1 (Agent Info)
                        TextObject info = new TextObject("{=R8t5y2U1}Roguery: {SKILL} | HP: {HP}%");
                        info.SetTextVariable("SKILL", h.GetSkillValue(DefaultSkills.Roguery));
                        info.SetTextVariable("HP", h.HitPoints);

                        spies.Add(new InquiryElement(h, $"{h.Name} ({info})", new CharacterImageIdentifier(CharacterCode.CreateFrom(h.CharacterObject))));
                    }
                }
            }

            if (spies.Count == 0)
            {
                // ID: L3k6n2P9 (No Agents)
                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=L3k6n2P9}No agents available (Roguery 30+ required).").ToString(), Colors.Red));
                return;
            }

            // IDs: P4s2m1K9 (Title), X8v6r3N2 (Desc), H5j2b8M4 (Deploy), W9q1z7V3 (Cancel)
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("{=P4s2m1K9}Infiltration Mission").ToString(),
                new TextObject("{=X8v6r3N2}Select an agent for the operation.").ToString(),
                spies,
                true,
                1,
                1,
                new TextObject("{=H5j2b8M4}Deploy").ToString(),
                new TextObject("{=W9q1z7V3}Cancel").ToString(),
                list => _spyManager.DeploySpy((Hero)list[0].Identifier, Settlement.CurrentSettlement),
                list => { },
                "",
                false
            ));
        }
    }
}