using SprocketExpedition.Game;

internal static class FrameWorkChecks
{
    internal static void Run()
    {
        static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
        var executed = 0; var disposed = 0; var time = 0d;
        IEnumerable<bool> Steps(int count, double cost)
        {
            try { for (var i = 0; i < count; i++) { executed++; time += cost; yield return true; } }
            finally { disposed++; }
        }
        var loader = new CampaignFrameWork(Steps(23, .7).GetEnumerator(), () => time);
        while (!loader.Complete)
        {
            var before = executed;
            loader.Advance();
            Check(executed - before <= 6, "four millisecond budget yields after at most one indivisible step overshoot");
        }
        Check(executed == 23 && loader.Steps == 23 && loader.Frames > 1 && disposed == 1,
            "all steps execute exactly once across frames, with one final cleanup");
        Check(loader.Advance() && disposed == 1, "completed work cannot rebuild or double-dispose the catalogue");
        var beforeCancel = executed;
        var cancel = new CampaignFrameWork(Steps(50, 0).GetEnumerator(), () => time);
        Check(!cancel.Advance(maximumSteps: 2) && executed == beforeCancel + 2,
            "step cap also bounds cheap work when the clock does not advance");
        cancel.Dispose(); cancel.Dispose();
        Check(disposed == 2 && executed == beforeCancel + 2, "session cancellation discards unfinished work without draining it");
        try { cancel.Advance(); throw new Exception("cancelled preparation resumed"); }
        catch (ObjectDisposedException) { }
        IEnumerable<bool> Broken()
        {
            try { yield return true; throw new InvalidDataException("fixture failure"); }
            finally { disposed++; }
        }
        var broken = new CampaignFrameWork(Broken().GetEnumerator(), () => time);
        try { broken.Advance(); throw new Exception("preparation failure was hidden"); }
        catch (InvalidDataException) { }
        Check(disposed == 3 && !broken.Complete, "failed preparation disposes without publishing a successful completion");
        var cards = new CampaignCardMetadataCache<string>();
        cards.SetContext("era-definitions-1");
        cards.Store("community/a.blueprint", 100, 5, "content-hash", "native card cost 100");
        Check(cards.TryGet("COMMUNITY/A.BLUEPRINT",100,5,"content-hash",out var header)&&header=="native card cost 100",
            "unchanged Windows file identity reuses the exact measured header");
        Check(!cards.TryGet("community/a.blueprint",101,5,"content-hash",out _)
            &&!cards.TryGet("community/a.blueprint",100,6,"content-hash",out _)
            &&!cards.TryGet("community/a.blueprint",100,5,"other-hash",out _)
            &&!cards.TryGet("community/b.blueprint",100,5,"content-hash",out _),
            "changed size, timestamp, content identity or path cannot reuse an old cost/era header");
        cards.SetContext("era-definitions-1");
        Check(cards.TryGet("community/a.blueprint",100,5,"content-hash",out _), "unchanged native eras retain the warm cache");
        cards.SetContext("era-definitions-2");
        Check(!cards.TryGet("community/a.blueprint",100,5,"content-hash",out _), "changed native era definitions discard cached classifications");
        Console.WriteLine("PASS frame-budgeted catalogue work, bounded steps, exact completion, cancellation and failure cleanup");
        Console.WriteLine("PASS process-local measured card reuse and file/hash/native-era invalidation");
    }
}
