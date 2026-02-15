#nullable enable
using BenchmarkDotNet.Attributes;
using GameBudget.Net;
using SlidingRank.FastOps;
using System;

[MemoryDiagnoser]
public class GameBudgetTopKBench
{
    [Params(TopKScenario.Unity_10k_D32_K200,
    TopKScenario.Studio_50k_D32_K1000,
    TopKScenario.Server_200k_D16_K2000)]
    public TopKScenario Scenario { get; set; }

    private ScenarioConfig _cfg;

    // inputs
    private float[] _featuresData = Array.Empty<float>();
    private float[] _weights = Array.Empty<float>();
    private int[] _ids = Array.Empty<int>();

    // baseline buffers (reused)
    private float[] _scores = Array.Empty<float>();
    private float[] _tmpScores = Array.Empty<float>();
    private int[] _tmpIds = Array.Empty<int>();

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

        _featuresData = new float[n * d];
        _weights = new float[d];
        _ids = new int[n];

        for (int i = 0; i < n; i++) _ids[i] = i + 1;
        for (int j = 0; j < d; j++) _weights[j] = (float)(rng.NextDouble() * 2 - 1);

        // Fill features:row-major
        for (int i = 0; i < _featuresData.Length; i++)
            _featuresData[i] = (float)(rng.NextDouble() * 2 - 1);

        _scores = new float[n];
        _tmpScores = new float[n];
        _tmpIds = new int[n];

        _outIds = new int[k];
        _outScores = new float[k];

        _sched = new FrameBudgetScheduler(maxEntities: n);
    }

    [Benchmark(Baseline = true, Description = "Baseline_ScalarDot_FullSort_TopK")]
    public float Baseline_ScalarDot_FullSort_TopK()
    {
        int n = _cfg.N;
        int d = _cfg.D;
        int k = _cfg.K;

        // 1) scalar scores
        for (int i = 0; i < n; i++)
        {
            float sum = 0;
            int off = i * d;
            for (int j = 0; j < d; j++)
                sum += _featuresData[off + j] * _weights[j];
            _scores[i] = sum;
        }

        // 2) copy to sort buffers (simulate common “sort all” approach)
        Array.Copy(_scores, _tmpScores, n);
        Array.Copy(_ids, _tmpIds, n);

        // 3) sort by score descending (Array.Sort ascending + reverse read)
        Array.Sort(_tmpScores, _tmpIds);

        // 4) take topK
        int take = Math.Min(k, n);
        for (int i = 0; i < take; i++)
        {
            int src = (n - 1) - i;
            _outIds[i] = _tmpIds[src];
            _outScores[i] = _tmpScores[src];
        }

        // checksum
        return _outScores[0] + _outScores[take - 1];
    }

    [Benchmark(Description = "Optimized_SlidingRankSIMD_TopK_NoFullSort")]
    public float Optimized_SlidingRankSIMD_TopK_NoFullSort()
    {
        var mat = new EmbeddingMatrix(_featuresData, _cfg.N, _cfg.D);

        int got = _sched.SelectTopK(
        features: mat,
        weights: _weights,
        ids: _ids,
        k: _cfg.K,
        outIds: _outIds,
        outScores: _outScores);

        return got > 0 ? (_outScores[0] + _outScores[got - 1]) : 0f;
    }
}
