GameBudget.Net

GameBudget.Net is a high-performance,zero-allocation selection toolkit for frame budget scheduling:
Compute scores = dot(features,weights) using SIMD (via SlidingRank.FastOps).
Select Top-K without sorting the whole population.
Select Top candidates with per-group quotas (e.g.,“animations”,“AI”,“VFX” each gets a slice) without full sort.

Repository:https://github.com/likeslines-maker/GameBudget.Net

Why

In real-time games and servers you often need to pick “the best few” entities to update this frame:
N can be 10k–200k+
D (embedding dimension) can be 16–64
K is usually hundreds to a few thousand
Sometimes you need fairness:quotas per group/type

A full sort is typically O(N log N) and wastes work when you only need the top K. GameBudget.Net uses:
SIMD dot products for scoring
streaming min-heap Top-K selection:O(N log K)
group quota selection using a heap per group segment:O(N log qᵍ)

Key advantages

Fast:avoids full sort,uses SIMD dot products.
Zero allocations in hot path:API is Span<T>/ReadOnlySpan<T>,caller-provided buffers; internal arrays are pooled and reused.
Designed for frame scheduling:Top-K or per-group quotas out of the box.
Predictable:output is sorted by score descending (within Top-K,and within each group segment for quotas).
Easy integration:works with plain float[] row-major data.

Dependency:scoring uses SlidingRank.FastOps (SIMD dot). Make sure its license/terms also fit your project.

---

Performance (BenchmarkDotNet)

Machine / runtime
Windows 11,Intel Core i5-11400F
.NET 8.0.24,RyuJIT x64

Top-K (dot + select Top-K)
| Scenario | Baseline:Scalar dot + full sort | GameBudget:SIMD dot + Top-K heap | Speedup |
|---|---:|---:|---:|
| N=10,000 D=32 K=200 | 709.3 μs | 111.7 μs | 6.35× |
| N=50,000 D=32 K=1000 | 4,256.6 μs | 726.2 μs | 5.86× |
| N=200,000 D=16 K=2000 | 16,157.5 μs | 2,063.4 μs | 7.83× |

Quotas (dot + select with group quotas)
| Scenario | Baseline:Scalar dot + full sort + quota pick | GameBudget:SIMD dot + per-group heaps | Speedup |
|---|---:|---:|---:|
| N=10,000 D=32 G=8 K=200 | 1,364.9 μs | 122.7 μs | 11.12× |
| N=50,000 D=64 G=16 K=1000 | 9,553.8 μs | 1,106.0 μs | 8.64× |
| N=200,000 D=32 G=32 K=2000 | 35,216.1 μs | 2,944.0 μs | 11.96× |

Baseline is a common approach:scalar dot for all entities,then full sort of all N items.

---

How it differs from typical alternatives

Versus Array.Sort / “sort indices then take K”
Sorting is O(N log N) even if you only need K.
GameBudget.Net is O(N log K) (or per-group O(N log qᵍ)).

Versus LINQ / allocations-heavy pipelines
LINQ often allocates and creates GC pressure.
GameBudget.Net is Span-based and designed to be allocation-free per frame.

Versus generic priority-queue snippets
Many implementations compute scores in scalar loops.
GameBudget.Net uses SIMD dot (SlidingRank) + selection primitives + pooled scheduler.

---

Installation

Once published to NuGet:
```bash
dotnet add package GameBudget.Net
```

If you’re using the repo directly,add a project reference and ensure SlidingRank is available.

---

Usage

1) Top-K selection
```csharp
using GameBudget.Net;
using SlidingRank.FastOps;

int n = 50_000;
int d = 32;
int k = 1000;

// Row-major features:[row0(d floats),row1(d floats),...]
float[] featuresData = new float[n * d];
float[] weights = new float[d];
int[] ids = new int[n];

// Fill data...
for (int i = 0; i < n; i++) ids[i] = i + 1;

var mat = new EmbeddingMatrix(featuresData,n,d);

using var sched = new FrameBudgetScheduler(maxEntities:n);

int[] outIds = new int[k];
float[] outScores = new float[k];

int got = sched.SelectTopK(
 features:mat,
 weights:weights,
 ids:ids,
 k:k,
 outIds:outIds,
 outScores:outScores
);

// outIds/outScores[0..got) sorted by score desc
```

2) Selection with per-group quotas

You provide:
groupId[i] in [0..G-1]
quotas[g] — how many to pick from each group
Output layout:group 0 segment,then group 1 segment,...
Each group segment is sorted by score descending
```csharp
using GameBudget.Net;
using SlidingRank.FastOps;

int n = 200_000;
int d = 32;
int groups = 32;
int k = 2000;

float[] featuresData = new float[n * d];
float[] weights = new float[d];
int[] ids = new int[n];
int[] groupId = new int[n];

// Example quotas:sum to K (you decide the policy)
int[] quotas = new int[groups];
for (int g = 0; g < groups; g++) quotas[g] = k / groups;
quotas[0] += k - (k / groups) * groups; // add remainder

var mat = new EmbeddingMatrix(featuresData,n,d);

using var sched = new FrameBudgetScheduler(maxEntities:n,maxGroups:groups);

int[] outIds = new int[k];
float[] outScores = new float[k];

int got = sched.SelectWithQuotas(
 features:mat,
 weights:weights,
 ids:ids,
 groupId:groupId,
 quotas:quotas,
 outIds:outIds,
 outScores:outScores
);
```

// Output is grouped by segments per group (each segment sorted by score desc).

Notes / contracts
ids.Length == features.Count
weights.Length == features.Dim
For quotas:
 - groupId.Length == ids.Length == scores.Length
 - quotas.Length == groupCount
 - group ids must be within [0..groupCount-1]
Provide outIds/outScores with enough capacity (K or sum(quotas)).

---

Pricing (commercial)

GameBudget.Net is commercial software.

Popular & accessible tiers
Indie — $49 / year
 For individuals and small teams (up to 5 developers),commercial use allowed.
Studio — $199 / year
 For teams up to 25 developers.
Enterprise — $999 / year
 Unlimited developers within one organization + priority support.

Free evaluation:14 days (no redistribution).

To purchase,request an invoice,or discuss custom terms,contact us.

---

License

This project uses a Commercial License (see LICENSE).
You may evaluate,but production/commercial use requires a paid license.

---

Contacts

Email:vipvodu@yandex.ru
Telegram:@vipvodu
Website:https://principium.pro
GitHub:https://github.com/likeslines-maker/GameBudget.Net
