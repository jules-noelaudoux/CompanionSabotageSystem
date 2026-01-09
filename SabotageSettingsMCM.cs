using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace CompanionSabotageSystem
{
    // Cette classe ne sera instanciée QUE si MCM est détecté par SettingsProvider.
    public class SabotageSettingsMCM : AttributeGlobalSettings<SabotageSettingsMCM>
    {
        public override string Id => "CompanionSabotageSystem";
        public override string DisplayName => "Companion Sabotage System";
        public override string FolderName => "CompanionSabotage";
        public override string FormatType => "json";

        [SettingPropertyInteger("{=css_set_xp}Roguery XP Gain", 0, 5000, "0 XP", Order = 1, RequireRestart = false, HintText = "{=css_set_xp_hint}XP gained by the companion after a successful mission.")]
        [SettingPropertyGroup("General")]
        public int XpGain { get; set; } = 800;

        [SettingPropertyFloatingInteger("{=css_set_capture}Capture Chance Multiplier", 0.1f, 5.0f, "0.0x", Order = 2, RequireRestart = false, HintText = "{=css_set_capture_hint}Multiplies the risk of being caught. Higher is harder.")]
        [SettingPropertyGroup("Difficulty")]
        public float CaptureChanceMultiplier { get; set; } = 1.0f;

        [SettingPropertyInteger("{=css_set_travel}Travel Speed (Days per distance)", 1, 10, "0 days", Order = 3, RequireRestart = false, HintText = "{=css_set_travel_hint}How fast spies travel. Lower is faster.")]
        [SettingPropertyGroup("General")]
        public int TravelSpeedDivisor { get; set; } = 50;

        [SettingPropertyInteger("{=css_set_food}Food Sabotage Amount", 1, 100, "0", Order = 4, RequireRestart = false, HintText = "{=css_set_food_hint}Base amount of food destroyed.")]
        [SettingPropertyGroup("Sabotage Impact")]
        public int FoodSabotageBase { get; set; } = 20;
    }
}