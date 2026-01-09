using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace CompanionSabotageSystem
{
    public class SabotageSaveDefiner : SaveableTypeDefiner
    {
        public SabotageSaveDefiner() : base(854_120_333) { }

        protected override void DefineClassTypes()
        {
            AddClassDefinition(typeof(SpyData), 1);
            // AJOUT IMPORTANT : On définit SpyManager pour qu'il puisse être sauvegardé
            AddClassDefinition(typeof(SpyManager), 2);
        }

        protected override void DefineEnumTypes()
        {
            AddEnumDefinition(typeof(SpyState), 3);
        }

        protected override void DefineContainerDefinitions()
        {
            ConstructContainerDefinition(typeof(Dictionary<Hero, SpyData>));
        }
    }
}