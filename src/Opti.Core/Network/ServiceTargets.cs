namespace Opti.Core.Network;

/// <summary>Сервисы, до которых игроку нужно достучаться. Первый — контрольный.</summary>
public static class ServiceTargets
{
    public static readonly IReadOnlyList<(string Name, string Host, int Port)> All = new[]
    {
        ("Microsoft", "www.microsoft.com", 443),
        ("Steam", "store.steampowered.com", 443),
        ("Epic Games", "www.epicgames.com", 443),
        ("Discord", "discord.com", 443),
        ("Riot Games", "www.riotgames.com", 443),
        ("Battle.net", "www.battle.net", 443),
    };
}
