namespace SprocketExpedition.Game;

internal static class CampaignTargetCommitment
{
    private const double MinimumCommitmentSeconds = 3.0;
    private const double RequiredImprovementRatio = 0.8;
    private const long AssignedPenalty = 45L;

    public record struct Candidate(long Key, float Distance, int Assigned, bool ClearShot);

    public sealed class Tracker
    {
        private long _targetKey;
        private double _committedAt;
        private float _lastNow;
        private bool _hasLastNow;

        public long Choose(IReadOnlyList<Candidate> candidates, float now)
        {
            if (!float.IsFinite(now))
            {
                Clear();
                return 0L;
            }

            if (_hasLastNow && now < _lastNow)
            {
                Clear();
            }

            _lastNow = now;
            _hasLastNow = true;

            if (candidates is null || candidates.Count == 0)
            {
                Clear();
                return 0L;
            }

            int bestClearIndex = -1;
            double bestClearScore = double.PositiveInfinity;
            int bestBlockedIndex = -1;
            double bestBlockedScore = double.PositiveInfinity;

            int bestClearAlternativeIndex = -1;
            double bestClearAlternativeScore = double.PositiveInfinity;
            int bestBlockedAlternativeIndex = -1;
            double bestBlockedAlternativeScore = double.PositiveInfinity;

            long currentKey = _targetKey;
            bool currentFound = false;
            bool currentClearShot = false;
            double currentScore = double.PositiveInfinity;

            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate candidate = candidates[i];
                if (!IsValid(candidate))
                {
                    continue;
                }

                double score = Score(candidate);
                bool isCurrent = currentKey != 0L && candidate.Key == currentKey;

                if (candidate.ClearShot)
                {
                    if (score < bestClearScore)
                    {
                        bestClearScore = score;
                        bestClearIndex = i;
                    }

                    if (!isCurrent && score < bestClearAlternativeScore)
                    {
                        bestClearAlternativeScore = score;
                        bestClearAlternativeIndex = i;
                    }
                }
                else
                {
                    if (score < bestBlockedScore)
                    {
                        bestBlockedScore = score;
                        bestBlockedIndex = i;
                    }

                    if (!isCurrent && score < bestBlockedAlternativeScore)
                    {
                        bestBlockedAlternativeScore = score;
                        bestBlockedAlternativeIndex = i;
                    }
                }

                if (isCurrent)
                {
                    currentFound = true;
                    currentClearShot = candidate.ClearShot;
                    currentScore = score;
                }
            }

            if (currentFound)
            {
                if (!currentClearShot)
                {
                    if (bestClearIndex >= 0)
                    {
                        return SwitchTo(candidates[bestClearIndex].Key, now);
                    }

                    double blockedHeldFor = now - _committedAt;
                    if (blockedHeldFor >= MinimumCommitmentSeconds &&
                        bestBlockedAlternativeIndex >= 0 &&
                        bestBlockedAlternativeScore < RequiredImprovementRatio * currentScore)
                    {
                        return SwitchTo(candidates[bestBlockedAlternativeIndex].Key, now);
                    }
                }
                else
                {
                    double clearHeldFor = now - _committedAt;
                    if (clearHeldFor >= MinimumCommitmentSeconds &&
                        bestClearAlternativeIndex >= 0 &&
                        bestClearAlternativeScore < RequiredImprovementRatio * currentScore)
                    {
                        return SwitchTo(candidates[bestClearAlternativeIndex].Key, now);
                    }
                }

                return _targetKey;
            }

            int selectedIndex = bestClearIndex >= 0 ? bestClearIndex : bestBlockedIndex;
            if (selectedIndex < 0)
            {
                Clear();
                return 0L;
            }

            return SwitchTo(candidates[selectedIndex].Key, now);
        }

        public void Clear()
        {
            _targetKey = 0L;
            _committedAt = 0.0;
            _lastNow = 0f;
            _hasLastNow = false;
        }

        private long SwitchTo(long key, float now)
        {
            _targetKey = key;
            _committedAt = now;
            return key;
        }

        private static bool IsValid(Candidate candidate)
        {
            return candidate.Key != 0L &&
                   candidate.Distance >= 0f &&
                   float.IsFinite(candidate.Distance);
        }

        private static double Score(Candidate candidate)
        {
            long assigned = candidate.Assigned > 0 ? candidate.Assigned : 0L;
            long penalty = assigned * AssignedPenalty;
            return (double)candidate.Distance + penalty;
        }
    }
}
