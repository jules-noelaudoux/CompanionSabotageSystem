using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions; // Pour TakePrisonerAction et TeleportHeroAction
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
        // --- SINGLETON ---
        public static SabotageCampaignBehavior Instance { get; private set; }

        public SabotageCampaignBehavior()
        {
            Instance = this;
        }

        private Dictionary<Hero, SpyData> _activeSpies = new Dictionary<Hero, SpyData>();

        // --- EVENTS & SYNC ---
        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            // On passe en HourlyTick pour une gestion plus fine du temps
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_activeSpies", ref _activeSpies);
        }

        // --- MENU DU JEU ---
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption("town", "sabotage_mission", "{=sabotage_opt}Send an Agent (Sabotage)",
                args =>
                {
                    args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
                    if (Settlement.CurrentSettlement == null || Settlement.CurrentSettlement.IsVillage) return false;

                    // Pas d'espionnage chez soi ou si assiégé
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
                    // Condition : Héros vivant, actif et Roguery >= 30
                    if (h.IsAlive && h.HeroState != Hero.CharacterStates.Disabled && h.GetSkillValue(DefaultSkills.Roguery) >= 30)
                    {
                        string info = $"Roguery: {h.GetSkillValue(DefaultSkills.Roguery)} | HP: {h.HitPoints}%";
                        spies.Add(new InquiryElement(h, $"{h.Name} ({info})", new CharacterImageIdentifier(CharacterCode.CreateFrom(h.CharacterObject))));
                    }
                }
            }

            if (spies.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage("No capable agents available (Requires Roguery 30+).", Colors.Red));
                return;
            }

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Infiltration Mission",
                "Select an agent to send for sabotage.",
                spies,
                true, 1, 1,
                "Deploy Agent", "Cancel",
                list => DeploySpy((Hero)list[0].Identifier),
                list => { },
                "", false
            ));
        }

        // --- DÉPLOIEMENT ---
        private void DeploySpy(Hero spy)
        {
            Settlement target = Settlement.CurrentSettlement;

            // On sort du menu pour revenir à la carte (bonne pratique UI)
            GameMenu.ExitToLast();

            // Appel au Manager pour la logique d'abstraction
            SpyManager.StartMission(spy, target);
        }

        // --- ENREGISTREMENT ---
        public void RegisterSpyMission(Hero spy, Settlement target, float travelHours)
        {
            if (!_activeSpies.ContainsKey(spy))
            {
                // On crée la mission abstraite
                var data = new SpyData(spy, target, travelHours, SpyState.TravelingToTarget);
                _activeSpies.Add(spy, data);
            }
        }

        // --- BOUCLE LOGIQUE (HOURLY TICK) ---
        private void OnHourlyTick()
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

                // Décrémentation du timer
                if (data.HoursRemaining > 0)
                {
                    data.HoursRemaining -= 1f;
                }

                switch (data.State)
                {
                    case SpyState.TravelingToTarget:
                        if (data.HoursRemaining <= 0)
                        {
                            // Arrivée "Virtuelle"
                            data.State = SpyState.Infiltrating;
                            // Durée de la mission sur place (ex: 5 jours = 120 heures)
                            data.HoursRemaining = 5 * 24f;

                            InformationManager.DisplayMessage(new InformationMessage($"{spy.Name} has infiltrated {data.TargetSettlement.Name}. Operation starting.", Colors.Yellow));
                        }
                        break;

                    case SpyState.Infiltrating:
                        // On vérifie le statut chaque jour (toutes les 24h restantes, ex: 96, 72, 48...)
                        // Utilisation du modulo pour exécuter une fois par jour
                        if (Math.Abs(data.HoursRemaining % 24) < 0.1f && data.HoursRemaining > 0)
                        {
                            bool captured = CheckForCapture(spy, data.TargetSettlement);
                            if (captured)
                            {
                                toRemove.Add(spy);
                            }
                            else
                            {
                                PerformSabotage(data);
                            }
                        }

                        // Fin de mission
                        if (data.HoursRemaining <= 0)
                        {
                            StartReturnJourney(spy, data);
                        }
                        break;

                    case SpyState.ReturningToPlayer:
                        // Retour terminé
                        if (data.HoursRemaining <= 0)
                        {
                            ReturnSpyFinal(spy, data);
                            toRemove.Add(spy);
                        }
                        break;
                }
            }

            foreach (var h in toRemove) _activeSpies.Remove(h);
        }

        // --- LOGIQUE INTERNE ---
        private bool CheckForCapture(Hero spy, Settlement target)
        {
            // Paramètre de difficulté récupéré depuis les Settings (si tu utilises MCM, sinon 1.0f par défaut)
            float difficultyMult = SabotageSettings.Instance != null ? SabotageSettings.Instance.DifficultyFactor : 1.0f;

            float security = target.Town.Security;
            float skill = spy.GetSkillValue(DefaultSkills.Roguery);
            float riskFactor = ((security * 1.2f) - skill) * difficultyMult;

            if (riskFactor < 2) riskFactor = 2; // Toujours 2% de chance minimum de se faire chopper

            if (MBRandom.RandomFloat * 100 < riskFactor)
            {
                // Si le héros était "virtuel", on s'assure qu'il est "Active" pour être prisonnier
                if (spy.HeroState == Hero.CharacterStates.Disabled)
                    spy.ChangeState(Hero.CharacterStates.Active);

                // On téléporte le héros dans la prison de la ville avant de le déclarer prisonnier
                // Cela évite qu'il soit prisonnier "dans le vide"
                TeleportHeroAction.ApplyImmediateTeleportToSettlement(spy, target);

                ShowSpyResultPopup(spy, "Agent Captured!", $"{spy.Name} has been caught by the guards of {target.Name}!\nThey are now rotting in the dungeon.", "Damn it!");

                // Action officielle de capture
                TakePrisonerAction.Apply(target.Party, spy);
                return true;
            }
            return false;
        }

        private void PerformSabotage(SpyData data)
        {
            Settlement target = data.TargetSettlement;
            if (target.Town == null) return; // Sécurité chateau/ville

            float skillFactor = data.Agent.GetSkillValue(DefaultSkills.Roguery) / 100f;

            // Food Sabotage
            if (target.Town.FoodStocks > 0)
            {
                float dmg = ((target.Town.FoodStocks * 0.10f) + 5f) * (0.8f + skillFactor);
                dmg = Math.Min(dmg, 50f);
                target.Town.FoodStocks -= dmg;
                data.TotalFoodDestroyed += (int)dmg;
            }

            // Loyalty Sabotage
            float loyaltyDmg = (1.0f + ((100f - target.Town.Security) / 20f)) * (0.5f + skillFactor);
            target.Town.Loyalty -= loyaltyDmg;
            data.TotalLoyaltyLost += loyaltyDmg;

            // Security Sabotage
            target.Town.Security -= 1.0f * (0.5f + skillFactor);
        }

        private void StartReturnJourney(Hero spy, SpyData data)
        {
            // CORRECTION : Utilisation de .Position et .DistanceSquared
            float distSq = MobileParty.MainParty.Position.DistanceSquared(data.TargetSettlement.Position);
            float rawDistance = (float)Math.Sqrt(distSq);

            float returnHours = (rawDistance * 1.25f) / 5.0f;
            if (returnHours < 2f) returnHours = 2f;

            data.State = SpyState.ReturningToPlayer;
            data.HoursRemaining = returnHours;

            float xpMult = SabotageSettings.Instance != null ? SabotageSettings.Instance.XPGainMultiplier : 1.0f;
            spy.AddSkillXp(DefaultSkills.Roguery, 800 * xpMult);

            InformationManager.DisplayMessage(new InformationMessage($"Mission complete. {spy.Name} is returning to base ({Math.Ceiling(returnHours / 24f)} days).", Colors.Green));
        }

        private void ReturnSpyFinal(Hero spy, SpyData data)
        {
            if (spy.HeroState != Hero.CharacterStates.Active)
            {
                spy.ChangeState(Hero.CharacterStates.Active);
            }

            // Le héros réapparaît instantanément dans la party du joueur
            TeleportHeroAction.ApplyImmediateTeleportToParty(spy, MobileParty.MainParty);

            // Double sécurité pour l'ajouter au roster si TeleportHeroAction ne l'a pas fait (ça dépend des versions de l'API)
            if (!MobileParty.MainParty.MemberRoster.Contains(spy.CharacterObject))
            {
                MobileParty.MainParty.MemberRoster.AddToCounts(spy.CharacterObject, 1);
            }

            string statsReport = "";
            if (data.TotalFoodDestroyed > 0) statsReport += $"- Food Supplies Destroyed: {data.TotalFoodDestroyed}\n";
            if (data.TotalLoyaltyLost > 0) statsReport += $"- Loyalty Reduced: {data.TotalLoyaltyLost:F1}\n";
            if (string.IsNullOrEmpty(statsReport)) statsReport = "No significant damage caused.";

            if (SabotageSettings.Instance == null || SabotageSettings.Instance.ShowPopups)
            {
                ShowSpyResultPopup(
                    spy,
                    "Mission Accomplished",
                    $"{spy.Name} has returned from the shadows of {data.TargetSettlement.Name}.\n\nMission Report:\n{statsReport}\n(Roguery XP Gained)",
                    "Excellent"
                );
            }
            else
            {
                InformationManager.DisplayMessage(new InformationMessage($"{spy.Name} returned. Report: Food -{data.TotalFoodDestroyed}, Loyalty -{data.TotalLoyaltyLost:F1}", Colors.Green));
            }
        }

        private void ShowSpyResultPopup(Hero spy, string title, string description, string buttonText)
        {
            var spyImage = new CharacterImageIdentifier(CharacterCode.CreateFrom(spy.CharacterObject));

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                title,
                description,
                new List<InquiryElement> { new InquiryElement(spy, spy.Name.ToString(), spyImage) },
                true, 1, 1, buttonText, "",
                list => { }, list => { }, "", false
            ));
        }
    }
}
