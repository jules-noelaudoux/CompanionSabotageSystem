using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace CompanionSabotageSystem
{
    public static class SpyManager
    {
        public static void StartMission(Hero spy, Settlement target)
        {
            // CORRECTION : Utilisation de .Position (CampaignVec2) et .DistanceSquared
            float distSq = MobileParty.MainParty.Position.DistanceSquared(target.Position);
            float rawDistance = (float)Math.Sqrt(distSq);

            // Facteur 1.25x pour simuler le chemin non linéaire
            // Vitesse 5.0f
            float estimatedTravelHours = (rawDistance * 1.25f) / 5.0f;

            if (estimatedTravelHours < 2f) estimatedTravelHours = 2f;

            // Retirer le héros
            spy.PartyBelongedTo?.MemberRoster.AddToCounts(spy.CharacterObject, -1);

            SabotageCampaignBehavior.Instance.RegisterSpyMission(spy, target, estimatedTravelHours);

            InformationManager.DisplayMessage(new InformationMessage($"{spy.Name} is on route to {target.Name}.", Colors.Gray));
        }
    }
}
