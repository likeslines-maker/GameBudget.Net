#nullable enable
public readonly record struct ScenarioConfig(int N, int D, int K, int Groups)
{
    public override string ToString() => $"N={N},D={D},K={K},G={Groups}";
}

public enum TopKScenario
{
    Unity_10k_D32_K200,
    Studio_50k_D32_K1000,
    Server_200k_D16_K2000
}

public enum QuotasScenario
{
    Unity_10k_D32_G8_K200,
    Studio_50k_D64_G16_K1000,
    Server_200k_D32_G32_K2000
}

public static class Scenarios
{
    public static ScenarioConfig Get(TopKScenario s) => s switch
    {
        TopKScenario.Unity_10k_D32_K200 => new(10_000, 32, 200, 0),
        TopKScenario.Studio_50k_D32_K1000 => new(50_000, 32, 1000, 0),
        TopKScenario.Server_200k_D16_K2000 => new(200_000, 16, 2000, 0),
        _ => throw new System.ArgumentOutOfRangeException(nameof(s))
    };

    public static ScenarioConfig Get(QuotasScenario s) => s switch
    {
        QuotasScenario.Unity_10k_D32_G8_K200 => new(10_000, 32, 200, 8),
        QuotasScenario.Studio_50k_D64_G16_K1000 => new(50_000, 64, 1000, 16),
        QuotasScenario.Server_200k_D32_G32_K2000 => new(200_000, 32, 2000, 32),
        _ => throw new System.ArgumentOutOfRangeException(nameof(s))
    };
}
