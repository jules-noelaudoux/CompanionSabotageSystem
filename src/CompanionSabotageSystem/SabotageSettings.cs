using TaleWorlds.ModuleManager;

namespace CompanionSabotageSystem
{
    // C'est cette classe que tout le reste du mod utilisera.
    // Elle ne plante pas si MCM est absent.
    public static class SettingsProvider
    {
        private static bool? _isMcmPresent;
        private static bool IsMcmPresent
        {
            get
            {
                if (!_isMcmPresent.HasValue)
                {
                    // Vérifie si la DLL de MCM est chargée dans le domaine
                    _isMcmPresent = ModuleHelper.GetModuleInfo("Bannerlord.MBOptionScreen") != null;
                }
                return _isMcmPresent.Value;
            }
        }

        // --- Valeurs par défaut (si MCM absent) ---
        private const int DefaultXpGain = 800;
        private const float DefaultCaptureChance = 1.0f;
        private const int DefaultTravelSpeed = 50;
        private const int DefaultFoodSabotage = 20;

        // --- Propriétés accessibles publiquement ---

        public static int XpGain
            => IsMcmPresent ? GetMcmXpGain() : DefaultXpGain;

        public static float CaptureChanceMultiplier
            => IsMcmPresent ? GetMcmCaptureChance() : DefaultCaptureChance;

        public static int TravelSpeedDivisor
            => IsMcmPresent ? GetMcmTravelSpeed() : DefaultTravelSpeed;

        public static int FoodSabotageBase
            => IsMcmPresent ? GetMcmFoodSabotage() : DefaultFoodSabotage;


        // --- Méthodes d'isolation (Appelées seulement si IsMcmPresent est vrai) ---
        // Il est crucial de mettre ces appels dans des méthodes séparées pour éviter que le JIT ne charge la classe manquante.

        private static int GetMcmXpGain()
        {
            // On utilise la reflection ou un try-catch implicite via l'isolation pour éviter le crash
            try { return SabotageSettingsMCM.Instance?.XpGain ?? DefaultXpGain; }
            catch { return DefaultXpGain; }
        }

        private static float GetMcmCaptureChance()
        {
            try { return SabotageSettingsMCM.Instance?.CaptureChanceMultiplier ?? DefaultCaptureChance; }
            catch { return DefaultCaptureChance; }
        }

        private static int GetMcmTravelSpeed()
        {
            try { return SabotageSettingsMCM.Instance?.TravelSpeedDivisor ?? DefaultTravelSpeed; }
            catch { return DefaultTravelSpeed; }
        }

        private static int GetMcmFoodSabotage()
        {
            try { return SabotageSettingsMCM.Instance?.FoodSabotageBase ?? DefaultFoodSabotage; }
            catch { return DefaultFoodSabotage; }
        }
    }
}