namespace SpectrumNet
{
    /// <summary>
    /// Общие аудио-константы для спектрального анализа.
    /// </summary>
    public static class SharedConstants
    {
        // Здесь задаются значения по умолчанию для уровня децибелов и коэффициента усиления.
        public const float DefaultMinDb = -130f;    // Минимальный уровень. (-80)
        public const float DefaultMaxDb = -20f;       // Максимальный уровень (0 дБ).
        public const float DefaultAmplificationFactor = 5.0f;  // 1f
    }
}
