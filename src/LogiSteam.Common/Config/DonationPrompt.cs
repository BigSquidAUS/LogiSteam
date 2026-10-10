namespace LogiSteam.Common.Config;

/// <summary>
/// LogiSteam is free; every so often it asks, once, whether the user would like to donate.
/// Rare on purpose: often enough to be seen, never often enough to be a nag.
/// </summary>
public static class DonationPrompt
{
    public const int EveryRuns = 50;

    /// <summary>True on the 50th, 100th, 150th... launch.</summary>
    public static bool IsDue(int runCount) => runCount > 0 && runCount % EveryRuns == 0;
}
