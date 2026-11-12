namespace Opti.Core.Games;

/// <summary>Какой лимит FPS ставить под монитор.</summary>
public static class FpsCap
{
    /// <summary>
    /// С переменной частотой (G-Sync / FreeSync) лимит ставят чуть ниже частоты монитора, чтобы не выходить за рабочий диапазон и держать
    /// очередь кадров пустой; обычная формула сообщества: f − f²/3600 (144 Гц → 138). Без VRR лимит равен частоте монитора.
    /// Это ориентир, а не закон: после установки проверьте ощущения и фреймтайм.
    /// </summary>
    public static int Recommend(int refreshHz, bool? vrr) =>
        refreshHz <= 0 ? 0 : vrr == true ? (int)Math.Floor(refreshHz - (double)refreshHz * refreshHz / 3600.0) : refreshHz;
}
