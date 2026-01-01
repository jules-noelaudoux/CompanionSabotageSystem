using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;

namespace CompanionSabotageSystem
{
    public class SabotageCampaignBehavior : CampaignBehaviorBase
    {
        private Dictionary<Hero, SpyData> _activeSpies = new Dictionary<Hero, SpyData>();

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_activeSpies", ref _activeSpies);
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption("town", "sabotage_mission", "{=sabotage_opt}Send an Agent",
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                    if (Settlement.CurrentSettlement == null || Settlement.CurrentSettlement.IsVillage) return false;

                    if (Settlement.CurrentSettlement.OwnerClan == Clan.PlayerClan || Settlement.CurrentSettlement.IsUnderSiege)
                        return false;

                    return true;
                },
                args => OpenSpySelectionList(), false, 2);
        }

        private void OpenSpySelectionList()
        {
            List<InquiryElement> spies = new List<InquiryElement>();

            foreach (var troop in MobileParty.MainParty.MemberRoster.GetTroopRoster())
            {
                if (troop.Character.IsHero && !troop.Character.IsPlayerCharacter)
                {
                    Hero h = troop.Character.HeroObject;
                    if (h.GetSkillValue(DefaultSkills.Roguery) >= 30 && h.HitPoints > 40)
                    {
                        string info = $"Roguery: {h.GetSkillValue(DefaultSkills.Roguery)} | HP: {h.HitPoints}%";
                        spies.Add(new InquiryElement(h, $"{h.Name} ({info})", new CharacterImageIdentifier(CharacterCode.CreateFrom(h.CharacterObject))));
                    }
                }
            }

            if (spies.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage("No agents available (Roguery 30+ required).", Colors.Red));
                return;
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Infiltration Mission",
                "Select an agent for the operation.",
                spies,
                true,
                1,
                1,
                "Deploy",
                "Cancel",
                list => DeploySpy((Hero)list[0].Identifier),
                list => { },
                "",
                false
            ));
        }

        private void DeploySpy(Hero spy)
        {
            Settlement target = Settlement.CurrentSettlement;

            float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(MobileParty.MainParty, target, false, MobileParty.NavigationType.Default, out _);
            int travelDays = (distance < 1f) ? 0 : (int)Math.Ceiling(distance / 50f);

            MobileParty.MainParty.MemberRoster.AddToCounts(spy.CharacterObject, -1);

            // On désactive le héros pour éviter les bugs de respawn vanilla
            spy.ChangeState(Hero.CharacterStates.Disabled);

            if (!_activeSpies.ContainsKey(spy))
            {
                SpyState initialState = (travelDays == 0) ? SpyState.Infiltrating : SpyState.TravelingToTarget;
                int initialDuration = (travelDays == 0) ? 5 : travelDays;

                _activeSpies.Add(spy, new SpyData(spy, target, initialDuration, initialState));

                if (travelDays > 0)
                    InformationManager.DisplayMessage(new InformationMessage($"{spy.Name} departs for {target.Name} (Travel: {travelDays} days).", Colors.Gray));
                else
                    InformationManager.DisplayMessage(new InformationMessage($"{spy.Name} vanishes into the shadows of {target.Name}...", Colors.Gray));
            }
        }

        private void OnDailyTick()
        {
            List<Hero> toRemove = new List<Hero>();
            List<Hero> activeHeroes = new List<Hero>(_activeSpies.Keys);

            foreach (var spy in activeHeroes)
            {
                if (spy == null || !spy.IsAlive)
                {
                    toRemove.Add(spy);
                    continue;
                }

                var data = _activeSpies[spy];
                data.DaysRemaining--;

                switch (data.State)
                {
                    case SpyState.TravelingToTarget:
                        if (data.DaysRemaining <= 0)
                        {
                            data.State = SpyState.Infiltrating;
                            data.DaysRemaining = 5;

                            // On ne téléporte pas physiquement le héros pour éviter qu'il soit capturé par le jeu vanilla.
                            // Il reste Disabled mais on commence la logique d'infiltration.
                            InformationManager.DisplayMessage(new InformationMessage($"{spy.Name} has arrived at {data.TargetSettlement.Name}. Operation starting.", Colors.Yellow));
                        }
                        break;

                    case SpyState.Infiltrating:
                        bool captured = CheckForCapture(spy, data.TargetSettlement);
                        if (captured)
                        {
                            toRemove.Add(spy);
                        }
                        else
                        {
                            PerformSabotage(data);

                            if (data.DaysRemaining <= 0)
                            {
                                StartReturnJourney(spy, data);
                            }
                        }
                        break;

                    case SpyState.ReturningToPlayer:
                        float distToPlayer = Campaign.Current.Models.MapDistanceModel.GetDistance(MobileParty.MainParty, data.TargetSettlement, false, MobileParty.NavigationType.Default, out _);
                        // On retourne si timer fini ou si joueur très proche
                        if (data.DaysRemaining <= 0 || distToPlayer < 5f)
                        {
                            ReturnSpyFinal(spy, data); // On passe 'data' pour afficher les stats
                            toRemove.Add(spy);
                        }
                        break;
                }
            }

            foreach (var h in toRemove) _activeSpies.Remove(h);
        }

        private bool CheckForCapture(Hero spy, Settlement target)
        {
            float security = target.Town.Security;
            float skill = spy.GetSkillValue(DefaultSkills.Roguery);

            float riskFactor = (security * 1.2f) - skill;
            if (riskFactor < 2) riskFactor = 2; // Min 2% risk

            if (MBRandom.RandomFloat * 100 < riskFactor)
            {
                // Capture confirmée
                // 1. On réactive le héros pour qu'il existe physiquement
                if (spy.HeroState == Hero.CharacterStates.Disabled)
                {
                    spy.ChangeState(Hero.CharacterStates.Active);
                }

                ShowSpyResultPopup(
                    spy,
                    "Agent Captured!",
                    $"{spy.Name} has been caught by the guards of {target.Name}!\nThey are now rotting in the dungeon.",
                    "Damn it!"
                );

                TakePrisonerAction.Apply(target.Party, spy);
                return true;
            }
            return false;
        }

        private void PerformSabotage(SpyData data)
        {
            Settlement target = data.TargetSettlement;
            float skillFactor = data.Agent.GetSkillValue(DefaultSkills.Roguery) / 100f;

            // 1. Food Sabotage
            float foodStocks = target.Town.FoodStocks;
            if (foodStocks > 0)
            {
                float rawDamage = (foodStocks * 0.10f) + 5f;
                float finalFoodDamage = rawDamage * (0.8f + skillFactor);
                finalFoodDamage = Math.Min(finalFoodDamage, 50f); // Cap safety

                target.Town.FoodStocks -= finalFoodDamage;
                data.TotalFoodDestroyed += (int)finalFoodDamage;
            }

            // 2. Loyalty Sabotage
            float security = target.Town.Security;
            float securityWeakness = (100f - security) / 20f;
            float rawLoyaltyDamage = 1.0f + (securityWeakness * 0.5f);
            float finalLoyaltyDamage = rawLoyaltyDamage * (0.5f + skillFactor);

            target.Town.Loyalty -= finalLoyaltyDamage;
            data.TotalLoyaltyLost += finalLoyaltyDamage;

            // 3. Security Sabotage
            target.Town.Security -= 1.0f * (0.5f + skillFactor);
        }

        private void StartReturnJourney(Hero spy, SpyData data)
        {
            float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(MobileParty.MainParty, data.TargetSettlement, false, MobileParty.NavigationType.Default, out _);
            int returnDays = (int)Math.Ceiling(distance / 45f);
            if (returnDays < 1) returnDays = 1;

            data.State = SpyState.ReturningToPlayer;
            data.DaysRemaining = returnDays;

            // On s'assure qu'il est bien Disabled pour le retour
            if (spy.HeroState != Hero.CharacterStates.Disabled)
                spy.ChangeState(Hero.CharacterStates.Disabled);

            InformationManager.DisplayMessage(new InformationMessage($"Mission complete. {spy.Name} is returning to base ({returnDays} days).", Colors.Green));
            spy.AddSkillXp(DefaultSkills.Roguery, 800);
        }

        private void ReturnSpyFinal(Hero spy, SpyData data)
        {
            // Réactivation forcée
            if (spy.HeroState != Hero.CharacterStates.Active)
            {
                spy.ChangeState(Hero.CharacterStates.Active);
            }

            TeleportHeroAction.ApplyImmediateTeleportToParty(spy, MobileParty.MainParty);

            if (!MobileParty.MainParty.MemberRoster.Contains(spy.CharacterObject))
            {
                MobileParty.MainParty.MemberRoster.AddToCounts(spy.CharacterObject, 1);
            }

            // Rapport de mission dynamique
            string statsReport = "";
            if (data.TotalFoodDestroyed > 0)
                statsReport += $"- Food Supplies Destroyed: {data.TotalFoodDestroyed}\n";
            if (data.TotalLoyaltyLost > 0)
                statsReport += $"- Loyalty Reduced: {data.TotalLoyaltyLost:F1}\n";

            if (string.IsNullOrEmpty(statsReport)) statsReport = "No significant damage caused.";

            ShowSpyResultPopup(
                spy,
                "Mission Accomplished",
                $"{spy.Name} has returned from the shadows of {data.TargetSettlement.Name}.\n\nMission Report:\n{statsReport}\n(Roguery XP Gained)",
                "Excellent"
            );
        }

        private void ShowSpyResultPopup(Hero spy, string title, string description, string buttonText)
        {
            var spyImage = new CharacterImageIdentifier(CharacterCode.CreateFrom(spy.CharacterObject));

            List<InquiryElement> elements = new List<InquiryElement>
            {
                new InquiryElement(spy, spy.Name.ToString(), spyImage)
            };

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                title,
                description,
                elements,
                true,
                1,
                1,
                buttonText,
                "",
                list => { },
                list => { },
                "",
                false
            ));
        }
    }
}