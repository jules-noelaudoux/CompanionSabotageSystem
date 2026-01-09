using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using TaleWorlds.Localization;

namespace CompanionSabotageSystem
{
    public class SabotageSettingsMCM : AttributeGlobalSettings<SabotageSettingsMCM>
    {
        public override string Id => "CompanionSabotageSystem";
        public override string DisplayName => "Companion Sabotage System";
        public override string FolderName => "CompanionSabotage";
        public override string FormatType => "json";

        // IDs: Q2w9e4R7, Y6u3i8O5
        [SettingPropertyInteger("{=Q2w9e4R7}Roguery XP Gain", 0, 5000, "0 XP", Order = 1, RequireRestart = false, HintText = "{=Y6u3i8O5}XP gained by the companion after a successful mission.")]
        [SettingPropertyGroup("General")]
        public int XpGain { get; set; } = 800;

        // IDs: A1s4d7F2, G3h6j9K5
        [SettingPropertyFloatingInteger("{=A1s4d7F2}Capture Chance Multiplier", 0.1f, 5.0f, "0.0x", Order = 2, RequireRestart = false, HintText = "{=G3h6j9K5}Multiplies the risk of being caught. Higher is harder.")]
        [SettingPropertyGroup("Difficulty")]
        public float CaptureChanceMultiplier { get; set; } = 1.0f;

        // IDs: L2z5x8C3, V6b9n4M7
        [SettingPropertyInteger("{=L2z5x8C3}Travel Speed (Days per distance)", 1, 10, "0 days", Order = 3, RequireRestart = false, HintText = "{=V6b9n4M7}How fast spies travel. Lower is faster.")]
        [SettingPropertyGroup("General")]
        public int TravelSpeedDivisor { get; set; } = 50;

        // IDs: Q8w5e2R4, T7y1u3I6
        [SettingPropertyInteger("{=Q8w5e2R4}Food Sabotage Amount", 1, 100, "0", Order = 4, RequireRestart = false, HintText = "{=T7y1u3I6}Base amount of food destroyed.")]
        [SettingPropertyGroup("Sabotage Impact")]
        public int FoodSabotageBase { get; set; } = 20;
    }
}