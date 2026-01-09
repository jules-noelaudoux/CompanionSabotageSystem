using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party; // Important pour MobileParty
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;
using TaleWorlds.Localization;

namespace CompanionSabotageSystem
{
    public class SpyManager
    {
        [SaveableField(1)]
        private Dictionary<Hero, SpyData> _activeSpies = new Dictionary<Hero, SpyData>();

        public void DeploySpy(Hero spy, Settlement target)
        {
            float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(MobileParty.MainParty, target, false, MobileParty.NavigationType.Default, out _);

            int divisor = SabotageSettings.Instance != null ? SabotageSettings.Instance.TravelSpeedDivisor : 50;
            int travelDays = (distance < 1f) ? 0 : (int)Math.Ceiling(distance / (float)divisor);

            MobileParty.MainParty.MemberRoster.AddToCounts(spy.CharacterObject, -1);
            spy.ChangeState(Hero.CharacterStates.Disabled);

            if (!_activeSpies.ContainsKey(spy))
            {
                SpyState initialState = (travelDays == 0) ? SpyState.Infiltrating : SpyState.TravelingToTarget;
                int initialDuration = (travelDays == 0) ? 5 : travelDays;

                _activeSpies.Add(spy, new SpyData(spy, target, initialDuration, initialState));

                if (travelDays > 0)
                {
                    TextObject msg = new TextObject("{=css_depart_travel}{AGENT} departs for {TARGET} ({DAYS} days).");
                    msg.SetTextVariable("AGENT", spy.Name);
                    msg.SetTextVariable("TARGET", target.Name);
                    msg.SetTextVariable("DAYS", travelDays);
                    InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Gray));
                }
                else
                {
                    TextObject msg = new TextObject("{=css_depart_instant}{AGENT} slips into {TARGET}...");
                    msg.SetTextVariable("AGENT", spy.Name);
                    msg.SetTextVariable("TARGET", target.Name);
                    InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Gray));
                }
            }
        }

        public void DailyTick()
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
                if (!_activeSpies.ContainsKey(spy)) continue;

                var data = _activeSpies[spy];

                // --- CORRECTIF ANTI-CRASH (Jailbreak) ---
                // Si l'espion est prisonnier, cela signifie qu'il a été capturé (par nous ou par le jeu).
                // Il faut impérativement arrêter de le gérer (le laisser Active/Prisoner) et le retirer du système.
                // Sinon, le Watchdog ci-dessous va le remettre en "Disabled", ce qui fera crasher la scène de prison.
                if (spy.IsPrisoner)
                {
                    toRemove.Add(spy);
                    continue;
                }
                // ----------------------------------------

                // Watchdog (Sécurité anti-réapparition taverne)
                // Ne s'applique que s'il n'est PAS prisonnier (géré au dessus) et PAS en retour.
                if (data.State != SpyState.ReturningToPlayer && spy.HeroState != Hero.CharacterStates.Disabled)
                {
                    spy.ChangeState(Hero.CharacterStates.Disabled);
                }

                // Security Check (Siège / Changement de faction)
                if (data.State != SpyState.ReturningToPlayer)
                {
                    if (data.TargetSettlement.IsUnderSiege || data.TargetSettlement.MapFaction == MobileParty.MainParty.MapFaction)
                    {
                        TextObject msg = new TextObject("{=css_mission_abort}Mission aborted: {TARGET} is unstable. {AGENT} is returning.");
                        msg.SetTextVariable("TARGET", data.TargetSettlement.Name);
                        msg.SetTextVariable("AGENT", spy.Name);
                        InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Red));

                        StartReturnJourney(spy, data);
                        continue;
                    }
                }

                data.DaysRemaining--;

                switch (data.State)
                {
                    case SpyState.TravelingToTarget:
                        if (data.DaysRemaining <= 0)
                        {
                            data.State = SpyState.Infiltrating;
                            data.DaysRemaining = 5;

                            TextObject msg = new TextObject("{=css_arrived}{AGENT} arrived at {TARGET}. Sabotage begins.");
                            msg.SetTextVariable("AGENT", spy.Name);
                            msg.SetTextVariable("TARGET", data.TargetSettlement.Name);
                            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Yellow));
                        }
                        break;

                    case SpyState.Infiltrating:
                        bool captured = CheckForCapture(spy, data.TargetSettlement);
                        if (captured)
                        {
                            // S'il est capturé, on l'ajoute à toRemove.
                            // Grâce au correctif en haut de boucle, s'il reste un tick, le Watchdog ne le touchera pas.
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
                        if (data.DaysRemaining <= 0 || distToPlayer < 5f)
                        {
                            ReturnSpyFinal(spy, data);
                            toRemove.Add(spy);
                        }
                        break;
                }
            }

            foreach (var h in toRemove)
            {
                if (_activeSpies.ContainsKey(h)) _activeSpies.Remove(h);
            }
        }

        private bool CheckForCapture(Hero spy, Settlement target)
        {
            float security = target.Town.Security;
            float skill = spy.GetSkillValue(DefaultSkills.Roguery);
            float multiplier = SabotageSettings.Instance != null ? SabotageSettings.Instance.CaptureChanceMultiplier : 1.0f;

            float riskFactor = ((security * 1.2f) - skill) * multiplier;
            if (riskFactor < 2) riskFactor = 2;

            if (MBRandom.RandomFloat * 100 < riskFactor)
            {
                // Capture : On réactive le héros pour qu'il puisse être prisonnier
                if (spy.HeroState == Hero.CharacterStates.Disabled)
                    spy.ChangeState(Hero.CharacterStates.Active);

                // On vérifie qu'il y a bien un parti pour le prendre (Garnison ou Parti lié)
                PartyBase jailer = target.Party ?? target.Town?.GarrisonParty?.Party;

                if (jailer != null)
                {
                    TakePrisonerAction.Apply(jailer, spy);

                    TextObject msg = new TextObject("{=css_captured_msg}{AGENT} has been captured in {TARGET}!");
                    msg.SetTextVariable("AGENT", spy.Name);
                    msg.SetTextVariable("TARGET", target.Name);
                    MBInformationManager.AddQuickInformation(msg);
                    return true;
                }
                else
                {
                    // Fallback si pas de garnison : Il rentre blessé
                    InformationManager.DisplayMessage(new InformationMessage("Capture failed (No Garrison), agent returning wounded.", Colors.Red));
                    return false; // Il continuera sa mission ou rentrera au prochain tick
                }
            }
            return false;
        }

        private void PerformSabotage(SpyData data)
        {
            Settlement target = data.TargetSettlement;
            float skillFactor = data.Agent.GetSkillValue(DefaultSkills.Roguery) / 100f;
            int baseFoodDamage = SabotageSettings.Instance != null ? SabotageSettings.Instance.FoodSabotageBase : 20;

            if (target.Town.FoodStocks > 0)
            {
                float damage = (baseFoodDamage + (target.Town.FoodStocks * 0.05f)) * (1f + skillFactor);
                target.Town.FoodStocks = Math.Max(0, target.Town.FoodStocks - damage);
                data.TotalFoodDestroyed += (int)damage;
            }

            target.Town.Loyalty -= 1f + skillFactor;
            target.Town.Security -= 1f + skillFactor;
            data.TotalLoyaltyLost += 1f + skillFactor;
        }

        private void StartReturnJourney(Hero spy, SpyData data)
        {
            float distance = Campaign.Current.Models.MapDistanceModel.GetDistance(MobileParty.MainParty, data.TargetSettlement, false, MobileParty.NavigationType.Default, out _);

            if (distance < 1.0f || MobileParty.MainParty.CurrentSettlement == data.TargetSettlement)
            {
                ReturnSpyFinal(spy, data);
                if (_activeSpies.ContainsKey(spy)) _activeSpies.Remove(spy);
                return;
            }

            int divisor = SabotageSettings.Instance != null ? SabotageSettings.Instance.TravelSpeedDivisor : 50;
            int returnDays = (int)Math.Ceiling(distance / (float)divisor);
            if (returnDays < 1) returnDays = 1;

            data.State = SpyState.ReturningToPlayer;
            data.DaysRemaining = returnDays;

            if (spy.HeroState != Hero.CharacterStates.Disabled)
                spy.ChangeState(Hero.CharacterStates.Disabled);

            TextObject msg = new TextObject("{=css_mission_done}{AGENT} mission done. Returning ({DAYS} days).");
            msg.SetTextVariable("AGENT", spy.Name);
            msg.SetTextVariable("DAYS", returnDays);
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));

            int xp = SabotageSettings.Instance != null ? SabotageSettings.Instance.XpGain : 800;
            spy.AddSkillXp(DefaultSkills.Roguery, xp);
        }

        private void ReturnSpyFinal(Hero spy, SpyData data)
        {
            if (spy.HeroState != Hero.CharacterStates.Active)
                spy.ChangeState(Hero.CharacterStates.Active);

            TeleportHeroAction.ApplyImmediateTeleportToParty(spy, MobileParty.MainParty);

            if (!MobileParty.MainParty.MemberRoster.Contains(spy.CharacterObject))
            {
                MobileParty.MainParty.MemberRoster.AddToCounts(spy.CharacterObject, 1);
            }

            if (data.State != SpyState.ReturningToPlayer)
            {
                int xp = SabotageSettings.Instance != null ? SabotageSettings.Instance.XpGain : 800;
                spy.AddSkillXp(DefaultSkills.Roguery, xp);
            }

            TextObject msg = new TextObject("{=css_back_in_party}{AGENT} is back in the party.");
            msg.SetTextVariable("AGENT", spy.Name);
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString(), Colors.Green));
        }

        public bool IsHeroBusy(Hero hero)
        {
            return _activeSpies.ContainsKey(hero);
        }
    }
}