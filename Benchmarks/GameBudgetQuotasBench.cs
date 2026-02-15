#nullable enable
using BenchmarkDotNet.Attributes;
using GameBudget.Net;
using SlidingRank.FastOps;
using System;

[MemoryDiagnoser]
public class GameBudgetQuotasBench
{
    [Params(QuotasScenario.Unity_10k_D32_G8_K200,
    QuotasScenario.Studio_50k_D64_G16_K1000,
    QuotasScenario.Server_200k_D32_G32_K2000)]
    public QuotasScenario Scenario { get; set; }

    private ScenarioConfig _cfg;

    // inputs
    private float[] _featuresData = Array.Empty<float>();
    private float[] _weights = Array.Empty<float>();
    private int[] _ids = Array.Empty<int>();
    private int[] _groupId = Array.Empty<int>();
    private int[] _quotas = Array.Empty<int>();

    // baseline buffers
    private float[] _scores = Array.Empty<float>();
    private float[] _tmpScores = Array.Empty<float>();
    private int[] _tmpIds = Array.Empty<int>();
    private int[] _tmpGroups = Array.Empty<int>();
    private int[] _quotaLeft = Array.Empty<int>();

    // optimized
    private FrameBudgetScheduler _sched = default!;

    // outputs
    private int[] _outIds = Array.Empty<int>();
    private float[] _outScores = Array.Empty<float>();

    [GlobalSetup]
    public void Setup()
    {
        _cfg = Scenarios.Get(Scenario);
        var rng = new Random(123);

        int n = _cfg.N;
        int d = _cfg.D;
        int k = _cfg.K;
        int g = _cfg.Groups;

        _featuresData = new float[n * d];
        _weights = new float[d];
        _ids = new int[n];
        _groupId = new int[n];

        for (int i = 0; i < n; i++) _ids[i] = i + 1;
        for (int j = 0; j < d; j++) _weights[j] = (float)(rng.NextDouble() * 2 - 1);

        for (int i = 0; i < _featuresData.Length; i++)
            _featuresData[i] = (float)(rng.NextDouble() * 2 - 1);

        // assign groups
        for (int i = 0; i < n; i++)
            _groupId[i] = rng.Next(0, g);

        // quotas sum = k (distributed)
        _quotas = new int[g];
        int baseQ = k / g;
        int rem = k - baseQ * g;
        for (int i = 0; i < g; i++) _quotas[i] = baseQ + (i < rem ? 1 : 0);

        _scores = new float[n];
        _tmpScores = new float[n];
        _tmpIds = new int[n];
        _tmpGroups = new int[n];
        _quotaLeft = new int[g];

        _outIds = new int[k];
        _outScores = new float[k];

        _sched = new FrameBudgetScheduler(maxEntities: n, maxGroups: g);
    }

    [Benchmark(Baseline = true, Description = "Baseline_ScalarDot_FullSort_QuotaPick")]
    public float Baseline_ScalarDot_FullSort_QuotaPick()
    {
        int n = _cfg.N;
        int d = _cfg.D;
        int g = _cfg.Groups;

        // 1) scalar scores
        for (int i = 0; i < n; i++)
        {
            float sum = 0;
            int off = i * d;
            for (int j = 0; j < d; j++)
                sum += _featuresData[off + j] * _weights[j];
            _scores[i] = sum;
        }

        // 2) copy to sort buffers
        Array.Copy(_scores, _tmpScores, n);
        Array.Copy(_ids, _tmpIds, n);
        Array.Copy(_groupId, _tmpGroups, n);

        // 3) sort by score asc (then iterate from end)
        Array.Sort(_tmpScores, _tmpIds);
        // Need groups in same order:we must permute _tmpGroups the same way.
        // The simplest allocation-free approach:sort indices instead.
        // But keep baseline simple:rebuild mapping via ids->group using original groupId dictionary would allocate.
        // Instead,do a baseline that sorts indices:
        return BaselineSortIndicesAndPick(n, d, g);
    }

    private float BaselineSortIndicesAndPick(int n, int d, int g)
    {
        // Sort indices by score (asc) without allocations:use tmpIds as indices buffer
        // We'll repurpose _tmpIds to store indices
        for (int i = 0; i < n; i++) _tmpIds[i] = i;

        // sort indices by score asc (custom quicksort)
        QuickSortByScore(_tmpIds, _scores, 0, n - 1);

        // reset quota left
        Array.Copy(_quotas, _quotaLeft, g);

        int written = 0;
        for (int p = n - 1; p >= 0 && written < _outIds.Length; p--)
        {
            int idx = _tmpIds[p];
            int gr = _groupId[idx];
            if (_quotaLeft[gr] <= 0) continue;

            _quotaLeft[gr]--;
            _outIds[written] = _ids[idx];
            _outScores[written] = _scores[idx];
            written++;
        }

        return written > 0 ? (_outScores[0] + _outScores[written - 1]) : 0f;
    }

    private static void QuickSortByScore(int[] idx, float[] scores, int left, int right)
    {
        while (left < right)
        {
            int i = left;
            int j = right;
            float pivot = scores[idx[(left + right) >>> 1]];

            while (i <= j)
            {
                while (scores[idx[i]] < pivot) i++;
                while (scores[idx[j]] > pivot) j--;
                if (i <= j)
                {
                    (idx[i], idx[j]) = (idx[j], idx[i]);
                    i++; j--;
                }
            }

            // recurse smaller part,loop larger (tail recursion elimination)
            if (j - left < right - i)
            {
                if (left < j) QuickSortByScore(idx, scores, left, j);
                left = i;
            }
            else
            {
                if (i < right) QuickSortByScore(idx, scores, i, right);
                right = j;
            }
        }
    }

    [Benchmark(Description = "Optimized_SlidingRankSIMD_Quotas_NoFullSort")]
    public float Optimized_SlidingRankSIMD_Quotas_NoFullSort()
    {
        var mat = new EmbeddingMatrix(_featuresData, _cfg.N, _cfg.D);

        int got = _sched.SelectWithQuotas(
        features: mat,
        weights: _weights,
        ids: _ids,
        groupId: _groupId,
        quotas: _quotas,
        outIds: _outIds,
        outScores: _outScores);

        return got > 0 ? (_outScores[0] + _outScores[got - 1]) : 0f;
    }
}
