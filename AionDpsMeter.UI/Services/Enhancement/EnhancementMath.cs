namespace AionDpsMeter.UI.Services.Enhancement
{
    /// <summary>One enhancement step (+N → +N+1): base success %, % added after each failure, cost per attempt.</summary>
    public sealed record EnhanceStep(int From, double SuccessPercent, double BonusPerFailPercent, long KinahPerTry, long StonesPerTry);

    public sealed record EnhanceOutcome(double Attempts, double Kinah, double Stones);

    public sealed record EnhanceResult(EnhanceOutcome Expected, EnhanceOutcome Lucky, EnhanceOutcome Typical, EnhanceOutcome Unlucky,
        IReadOnlyList<double> ExpectedAttemptsPerStep);

    /// <summary>
    /// AION 2 enhancement odds. Per grade the global client has (metabot.gg, Oct 2026):
    /// Common/Rare always succeed up to +5 (max +5); Epic always up to +10 (max +10);
    /// Unique always up to +10, then 65 / 50 / 35 / 25 / 20 % with +5 % per failed attempt (max +15).
    /// A failure only costs materials (gear never drops a level). The pity bonus resets after a success.
    /// </summary>
    public static class EnhancementMath
    {
        public static readonly (string Name, int Max, int Safe)[] Grades =
        [
            ("Common / Rare", 5, 5),
            ("Epic", 10, 10),
            ("Unique", 15, 10),
        ];

        private static readonly double[] UniqueRates = [65, 50, 35, 25, 20]; // +10→11 … +14→15
        private const double UniqueBonusPerFail = 5;

        /// <summary>The game's odds for a step (100 % up to the grade's safe level).</summary>
        public static (double Success, double Bonus) DefaultOdds(int gradeIndex, int from)
        {
            var (_, _, safe) = Grades[gradeIndex];
            if (from < safe) return (100, 0);
            int i = from - safe;
            return gradeIndex == 2 && i < UniqueRates.Length ? (UniqueRates[i], UniqueBonusPerFail) : (100, 0);
        }

        /// <summary>P(success exactly on attempt k), k = 1.. until the chance reaches 100 %.</summary>
        public static IReadOnlyList<double> AttemptDistribution(double successPercent, double bonusPercent)
        {
            var list = new List<double>();
            double stillFailing = 1.0;
            for (int k = 0; stillFailing > 1e-12 && k < 1000; k++)
            {
                double p = Math.Clamp((successPercent + k * bonusPercent) / 100.0, 0, 1);
                if (p <= 0 && bonusPercent <= 0) break; // 0 % and no pity: never succeeds
                list.Add(stillFailing * p);
                stillFailing *= 1 - p;
            }
            return list;
        }

        public static double ExpectedAttempts(double successPercent, double bonusPercent) =>
            AttemptDistribution(successPercent, bonusPercent).Select((p, i) => p * (i + 1)).Sum();

        /// <summary>Expected cost exactly; lucky / typical / unlucky = 10th / 50th / 90th percentile of simulated runs.</summary>
        public static EnhanceResult Calculate(IReadOnlyList<EnhanceStep> steps, int runs = 20_000, int seed = 2026)
        {
            var expectedPerStep = steps.Select(s => ExpectedAttempts(s.SuccessPercent, s.BonusPerFailPercent)).ToList();
            var expected = new EnhanceOutcome(
                expectedPerStep.Sum(),
                steps.Select((s, i) => expectedPerStep[i] * s.KinahPerTry).Sum(),
                steps.Select((s, i) => expectedPerStep[i] * s.StonesPerTry).Sum());

            // Cumulative distributions per step for sampling.
            var cdfs = steps.Select(s =>
            {
                var dist = AttemptDistribution(s.SuccessPercent, s.BonusPerFailPercent);
                var cdf = new double[dist.Count];
                double acc = 0;
                for (int i = 0; i < dist.Count; i++) cdf[i] = acc += dist[i];
                return cdf;
            }).ToList();

            var rng = new Random(seed);
            var samples = new (double Attempts, double Kinah, double Stones)[runs];
            for (int r = 0; r < runs; r++)
            {
                double attempts = 0, kinah = 0, stones = 0;
                for (int i = 0; i < steps.Count; i++)
                {
                    var cdf = cdfs[i];
                    if (cdf.Length == 0) continue;
                    double u = rng.NextDouble() * cdf[^1];
                    int k = Array.FindIndex(cdf, c => c >= u) + 1;
                    attempts += k;
                    kinah += k * (double)steps[i].KinahPerTry;
                    stones += k * (double)steps[i].StonesPerTry;
                }
                samples[r] = (attempts, kinah, stones);
            }

            EnhanceOutcome At(double q)
            {
                int idx = Math.Clamp((int)Math.Round(q * (runs - 1)), 0, runs - 1);
                // Percentile of each column separately (costs scale with attempts, so they rank together).
                return new EnhanceOutcome(
                    samples.Select(s => s.Attempts).OrderBy(v => v).ElementAt(idx),
                    samples.Select(s => s.Kinah).OrderBy(v => v).ElementAt(idx),
                    samples.Select(s => s.Stones).OrderBy(v => v).ElementAt(idx));
            }

            return new EnhanceResult(expected, At(0.10), At(0.50), At(0.90), expectedPerStep);
        }
    }
}
