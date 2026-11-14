namespace SprocketExpandedBallistics;

/// <summary>Verdict of <see cref="ChamberLedger{TKey}.Check"/>: may the gun fire the round it reports as loaded?</summary>
internal enum FireVerdict{Allowed,NoRound,TooEarly}

/// <summary>The next-load decision of a carried mix: the chosen type (null when nothing is left), whether it differs from the preference, and whether everything is empty.</summary>
internal readonly record struct AmmoChoice(string? Type,bool Switched,bool Empty);

/// <summary>
/// 0.22.7, pure. The rules behind "never fire without a real load" for a carried ammunition mix.
/// A shot needs a round that was really debited from the typed stock (a credit), and the load phases of that debit have to have had
/// their time. Everything the runtime does (request steering, the fire gate, the once-only refund) calls these, so the tests drive
/// exactly the logic the game runs.
/// </summary>
internal static class AmmoSelection
{
    /// <summary>
    /// The next-load type. The preference stays while it still has rounds; when it has run out the next carried type with rounds
    /// (searched forward from it, wrapping) is chosen and loads normally; with nothing left the gun stays empty (<see cref="AmmoChoice.Empty"/>).
    /// <paramref name="remaining"/> is the usable stock of a type (0 for a type the vehicle may not use, is not registered, or has no rounds).
    /// </summary>
    internal static AmmoChoice Choose(IReadOnlyList<AmmoLoadoutEntry> entries,string? preferred,Func<string,int> remaining)
    {
        if(entries.Count==0)return new(null,false,true);
        var start=preferred!=null&&AmmoLoadoutRules.IndexOf(entries,preferred)>=0?preferred:entries[0].ProfileId;
        if(remaining(start)>0)return new(start,start!=preferred,false);
        var next=AmmoLoadoutRules.Step(entries,remaining,start,1);
        return next==null?new(null,false,true):new(next,true,false);
    }
    /// <summary>The HUD line when nothing is left ("Out of HESH."), empty while any carried type has rounds.</summary>
    internal static string OutNotice(IReadOnlyList<AmmoLoadoutEntry> entries,string? preferred,Func<string,int> remaining,Func<string,string> label)
    {
        if(entries.Count==0||!Choose(entries,preferred,remaining).Empty)return "";
        var last=preferred!=null&&AmmoLoadoutRules.IndexOf(entries,preferred)>=0?preferred:entries[0].ProfileId;
        return "Out of "+label(last)+": no rounds left.";
    }
}

/// <summary>Once-only latch: the native refund of one occupied round may add exactly one round to the stock (a repeat would create ammunition).</summary>
internal sealed class RefundLatch
{
    internal bool Used{get;private set;}
    /// <summary>True the first time only.</summary>
    internal bool TryTake(){if(Used)return false;Used=true;return true;}
}

/// <summary>
/// Per load task: was the round in the gun really debited, and has its load had time? A debit is a credit; firing, unloading or cancelling
/// uses it up. A load that was started by <c>LoadInstantly</c> (the first chamber) and a round that was already in the gun when the mix was
/// stocked are exempt from the time rule. The time rule asks a quarter of the load phases the task reports (speeds of crews and mechanisms
/// vary, an instant load never reaches a quarter).
/// </summary>
internal sealed class ChamberLedger<TKey> where TKey:notnull
{
    /// <summary>Share of the reported load phases that must have passed between the debit and the shot.</summary>
    internal const double MinimumFraction=.25;
    /// <summary>A round that is reported loaded without a credit is a phantom after this many seconds; the runtime then empties the chamber so a real load can start.</summary>
    internal const double PhantomGraceSeconds=.5;
    private sealed record Credit(double DebitedAt,bool Exempt,string? Type,long Token);
    private long generation;
    private readonly Dictionary<TKey,Credit> credits=new();
    private readonly Dictionary<TKey,double> phantoms=new();
    private readonly HashSet<(TKey Key,FireVerdict Verdict)> logged=new();
    internal int Count=>credits.Count;
    internal bool Has(TKey key)=>credits.ContainsKey(key);
    /// <summary>A real debit from the typed stock at <paramref name="now"/> (<paramref name="instant"/>: inside LoadInstantly).</summary>
    internal void Debit(TKey key,double now,string? type=null,bool instant=false){credits[key]=new(now,instant,type,++generation);Forget(key);}
    /// <summary>A round that was in the gun before the mix was stocked (it was debited by the game).</summary>
    internal void Initial(TKey key,string? type=null){credits[key]=new(double.NegativeInfinity,true,type,++generation);Forget(key);}
    internal long Token(TKey key)=>credits.TryGetValue(key,out var credit)?credit.Token:0;
    // FireInternal can debit the next load before its postfix runs. Spend only the fired generation.
    internal void Spend(TKey key,long token){if(token!=0&&Token(key)==token)Spend(key);}
    /// <summary>The shot was fired, or the round was unloaded or cancelled: the credit is used up.</summary>
    internal void Spend(TKey key){credits.Remove(key);Forget(key);}
    internal void Reset(){credits.Clear();phantoms.Clear();logged.Clear();}
    internal string? TypeOf(TKey key)=>credits.TryGetValue(key,out var c)?c.Type:null;
    private void Forget(TKey key){phantoms.Remove(key);logged.RemoveWhere(l=>EqualityComparer<TKey>.Default.Equals(l.Key,key));}
    /// <summary>May the gun fire now? <paramref name="phaseSeconds"/> is the sum of the load phases the task reports (0 when unknown).</summary>
    internal FireVerdict Check(TKey key,double now,double phaseSeconds,out double neededSeconds,out double elapsedSeconds)
    {
        neededSeconds=0;elapsedSeconds=0;
        if(!credits.TryGetValue(key,out var credit))return FireVerdict.NoRound;
        if(credit.Exempt)return FireVerdict.Allowed;
        elapsedSeconds=now-credit.DebitedAt;
        neededSeconds=MinimumFraction*(double.IsFinite(phaseSeconds)?Math.Clamp(phaseSeconds,0,120):0);
        return elapsedSeconds+1e-9>=neededSeconds?FireVerdict.Allowed:FireVerdict.TooEarly;
    }
    /// <summary>First time this verdict is reported for the current round: log it once, not every frame.</summary>
    internal bool FirstTime(TKey key,FireVerdict verdict)=>logged.Add((key,verdict));
    /// <summary>
    /// A loaded round without a credit. False while it is new (a transient state), true once it has lasted <see cref="PhantomGraceSeconds"/>:
    /// the runtime then removes the phantom round.
    /// </summary>
    internal bool IsPhantom(TKey key,double now)
    {
        if(credits.ContainsKey(key)){phantoms.Remove(key);return false;}
        if(!phantoms.TryGetValue(key,out var since)){phantoms[key]=now;return false;}
        return now-since>=PhantomGraceSeconds;
    }
}

internal static class AmmoReloadPolicy
{
    internal static bool CanSteer(bool loading,bool holdsRound)=>!loading&&!holdsRound;
    internal static bool RecoverDebit(int before,int after,bool sameOwnedSource,bool occupied,bool consumed)
        =>sameOwnedSource&&occupied&&!consumed&&before>0&&after==before-1;
    internal static bool RecoverCycleDebit(int stockBefore,int stockAfter,int sourceBefore,int sourceAfter,bool occupied,bool consumed)
        =>stockBefore>0&&stockAfter>=0&&stockAfter==stockBefore-1&&RecoverDebit(sourceBefore,sourceAfter,true,occupied,consumed);
    internal static bool RemovePhantom(bool loaded,bool loading,bool credit,bool grace,bool noDebitProven,bool stockUnchanged,bool? externalLoader)
        =>loaded&&!loading&&!credit&&grace&&noDebitProven&&stockUnchanged&&externalLoader==false;
}
