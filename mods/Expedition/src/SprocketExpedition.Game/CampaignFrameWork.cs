using System.Diagnostics;

namespace SprocketExpedition.Game;

// Cooperative main-thread work: never sends native objects to a worker thread.
internal sealed class CampaignFrameWork : IDisposable
{
    private IEnumerator<bool>? work;
    private readonly Func<double> milliseconds;
    internal int Steps { get; private set; }
    internal int Frames { get; private set; }
    internal double PeakStepMilliseconds { get; private set; }
    internal bool Complete { get; private set; }

    internal CampaignFrameWork(IEnumerator<bool> work, Func<double>? milliseconds = null)
    {
        this.work = work;
        this.milliseconds = milliseconds ?? (() => Stopwatch.GetTimestamp() * (1000d / Stopwatch.Frequency));
    }

    internal bool Advance(int maximumSteps = 8, double budgetMilliseconds = 4)
    {
        if (Complete) return true;
        if (work == null) throw new ObjectDisposedException(nameof(CampaignFrameWork));
        if (maximumSteps < 1 || !double.IsFinite(budgetMilliseconds) || budgetMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds));
        Frames++;
        var start = milliseconds();
        try
        {
            for (var i = 0; i < maximumSteps; i++)
            {
                var before = milliseconds();
                var more = work.MoveNext();
                PeakStepMilliseconds = Math.Max(PeakStepMilliseconds, milliseconds() - before);
                if (!more) { Complete = true; Dispose(); return true; }
                Steps++;
                if (milliseconds() - start >= budgetMilliseconds) break;
            }
            return false;
        }
        catch { Dispose(); throw; }
    }

    public void Dispose() { var previous = work; work = null; previous?.Dispose(); }
}
