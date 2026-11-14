namespace SprocketExpandedBallistics;

/// <summary>
/// 0.22.7 item 6: a carried mix whose selected type runs out. The user case: a 125 mm Oplot with the carousel autoloader, mix heat/hesh,
/// HESH ran out while HEAT still had rounds, and the gun fired in rapid succession. The rules below are the pure ones the game runs
/// (AmmoSelection, ChamberLedger, RefundLatch).
/// </summary>
internal static class Fix0227AmmoTests
{
    internal static void Run(Action<bool,string> check)
    {
        var mix=new[]{new AmmoLoadoutEntry("heat",20),new AmmoLoadoutEntry("hesh",24)};
        var stock=new Dictionary<string,int>{["heat"]=20,["hesh"]=24};
        int Remaining(string id)=>stock.GetValueOrDefault(id);

        // ---- the next-load choice ----
        check(AmmoSelection.Choose(mix,"hesh",Remaining)==new AmmoChoice("hesh",false,false),"ammo: the preference stays while it has rounds");
        stock["hesh"]=0;
        var out1=AmmoSelection.Choose(mix,"hesh",Remaining);
        check(out1.Type=="heat"&&out1.Switched&&!out1.Empty,"ammo: HESH ran out, HEAT is the next load (the user case)");
        stock["hesh"]=24;stock["heat"]=0;
        check(AmmoSelection.Choose(mix,"heat",Remaining)==new AmmoChoice("hesh",true,false),"ammo: the first type running out hands over to the next");
        stock["heat"]=20;stock["hesh"]=0;
        var three=new[]{new AmmoLoadoutEntry("apds",5),new AmmoLoadoutEntry("heat",20),new AmmoLoadoutEntry("hesh",24)};
        stock["apds"]=5;
        check(AmmoSelection.Choose(three,"hesh",Remaining).Type=="apds","ammo: after the last type the search wraps to the first with rounds");
        stock["apds"]=0;stock["heat"]=0;stock["hesh"]=0;
        var none=AmmoSelection.Choose(three,"hesh",Remaining);
        check(none.Type==null&&none.Empty&&!none.Switched,"ammo: everything empty gives no choice (the gun stays empty)");
        stock["heat"]=20;stock["hesh"]=24;
        check(AmmoSelection.Choose(mix,"unknown",Remaining).Type=="heat"&&AmmoSelection.Choose(mix,null,Remaining).Type=="heat","ammo: a preference that is not carried falls back to the first row");
        check(AmmoSelection.Choose(Array.Empty<AmmoLoadoutEntry>(),"heat",Remaining).Empty,"ammo: an empty mix chooses nothing");
        // a type the vehicle may not use counts as empty (Usable passes 0 for it)
        check(AmmoSelection.Choose(mix,"hesh",id=>id=="hesh"?0:Remaining(id)).Type=="heat","ammo: a type that is not allowed is skipped like an empty one");
        stock["hesh"]=0;stock["heat"]=0;
        var notice=AmmoSelection.OutNotice(mix,"hesh",Remaining,id=>id.ToUpperInvariant());
        stock["heat"]=3;
        check(notice=="Out of HESH: no rounds left."&&AmmoSelection.OutNotice(mix,"hesh",Remaining,id=>id)=="","ammo: the HUD says 'Out of <type>' only when nothing is left");

        // ---- the chamber ledger: no shot without a real load, and a load takes its time ----
        const double Reload=9,Phases=.8*Reload; // the carousel reports pickup .2 R + travel .2 R + drop-off .4 R
        var ledger=new ChamberLedger<int>();double t=100;
        check(ledger.Check(1,t,Phases,out _,out _)==FireVerdict.NoRound,"ammo: a gun with no debit cannot fire");
        ledger.Debit(1,t,"hesh",instant:true);
        check(ledger.Check(1,t,Phases,out _,out _)==FireVerdict.Allowed,"ammo: the first chamber (LoadInstantly) is instant and may fire at once");
        ledger.Spend(1);
        for(int i=0;i<500;i++)if(ledger.Check(1,t+i*.01,Phases,out _,out _)!=FireVerdict.NoRound){check(false,"ammo: a fired round fires again without a new load");break;}
        check(true,"ammo: after the shot, 500 frames in a row give no second shot without a new load");
        // HESH is out; HEAT is requested; the debit at t starts a real load
        ledger.Debit(1,t,"heat");
        var needed=0d;
        check(ledger.Check(1,t,Phases,out needed,out var elapsed)==FireVerdict.TooEarly&&Math.Abs(needed-.25*Phases)<1e-9&&elapsed==0,"ammo: right after the debit the shot is refused (too early)");
        check(ledger.Check(1,t+.25*Phases-.01,Phases,out _,out _)==FireVerdict.TooEarly,"ammo: still too early just before a quarter of the load phases");
        check(ledger.Check(1,t+Reload,Phases,out _,out _)==FireVerdict.Allowed,"ammo: after the real load time the shot is allowed");
        check(ledger.Check(1,t,0,out _,out _)==FireVerdict.Allowed,"ammo: a task that reports no load phases is never held back");
        check(ledger.Check(1,t,double.NaN,out _,out _)==FireVerdict.Allowed&&ledger.Check(1,t,1e9,out var cap,out _)==FireVerdict.TooEarly&&cap<=30.0001,"ammo: bad phase values are bounded");
        ledger.Spend(1);

        // ---- the scenario, frame by frame: HESH runs out, HEAT loads normally, nobody fires without a load ----
        {
            var s=new Dictionary<string,int>{["heat"]=3,["hesh"]=1};
            int R(string id)=>s.GetValueOrDefault(id);
            var mixTwo=new[]{new AmmoLoadoutEntry("heat",3),new AmmoLoadoutEntry("hesh",1)};
            var l=new ChamberLedger<int>();string? pref="hesh";string? loadedType=null;double now=0,loadDone=-1;bool loaded=false;
            var shots=new List<(double At,string Type)>();var requests=new List<(double At,string? Type)>();
            for(int frame=0;frame<4000;frame++,now+=.01)
            {
                // the loader: with an empty gun and a choice, debit one round and load it for Reload seconds
                if(!loaded&&loadDone<0)
                {
                    var choice=AmmoSelection.Choose(mixTwo,pref,R);
                    if(choice.Type!=null)
                    {
                        pref=choice.Type;requests.Add((now,choice.Type));
                        s[choice.Type]--;l.Debit(1,now,choice.Type);loadedType=choice.Type;loadDone=now+Reload;
                    }
                }
                if(loadDone>=0&&now>=loadDone){loaded=true;loadDone=-1;}
                // the player holds the fire button
                if(loaded&&l.Check(1,now,Phases,out _,out _)==FireVerdict.Allowed){shots.Add((now,loadedType!));l.Spend(1);loaded=false;}
            }
            check(shots.Count==4&&shots.Select(x=>x.Type).SequenceEqual(new[]{"hesh","heat","heat","heat"}),"ammo: the four carried rounds are fired once each, HESH first, then HEAT ("+string.Join(",",shots.Select(x=>x.Type))+")");
            check(shots.Zip(shots.Skip(1),(a,b)=>b.At-a.At).All(gap=>gap>=Reload-.011),"ammo: every shot is a full reload after the previous one (gaps "+string.Join("/",shots.Zip(shots.Skip(1),(a,b)=>(b.At-a.At).ToString("0.0")))+")");
            check(requests.Count==4&&requests[1].Type=="heat"&&requests[0].Type=="hesh","ammo: HEAT is requested right after the last HESH round, once per load");
            check(s["heat"]==0&&s["hesh"]==0,"ammo: no round is created or lost");
            check(AmmoSelection.Choose(mixTwo,pref,R).Empty,"ammo: with everything fired nothing is requested (no endless loading of an empty type)");
        }

        // ---- the failure modes a guard must hold against ----
        {
            // a: the game reports a loaded gun every frame without any debit (a phantom load)
            var l=new ChamberLedger<int>();int allowed=0;
            for(int f=0;f<1000;f++)if(l.Check(1,f*.01,Phases,out _,out _)==FireVerdict.Allowed)allowed++;
            check(allowed==0,"ammo: a phantom load never fires, 1000 frames");
            check(!l.IsPhantom(1,0)&&!l.IsPhantom(1,ChamberLedger<int>.PhantomGraceSeconds-.01)&&l.IsPhantom(1,ChamberLedger<int>.PhantomGraceSeconds+.01),"ammo: a loaded round without a debit becomes a phantom after the grace, not before");
            l.Debit(1,5,"heat");
            check(!l.IsPhantom(1,6),"ammo: a debit makes the round real");
            l.Spend(1);l.IsPhantom(1,10);l.Spend(1);
            check(!l.IsPhantom(1,10.1),"ammo: removing a phantom restarts the grace");
            // b: real debits every frame with no load time (instant loads): at most one shot per quarter of the load phases
            var fast=new ChamberLedger<int>();var fastShots=0;
            for(int f=0;f<2000;f++)
            {
                var now=f*.01;fast.Debit(1,now,"heat"); // debits keep happening, each restarts the clock
                if(fast.Check(1,now,Phases,out _,out _)==FireVerdict.Allowed)fastShots++;
            }
            check(fastShots==0,"ammo: a debit repeated every frame never lets a shot through (each debit restarts the load clock)");
            // c: one debit, then the player keeps pressing: exactly one shot
            var one=new ChamberLedger<int>();one.Debit(1,0,"heat");int fired=0;
            for(int f=0;f<3000;f++){var now=f*.01;if(one.Check(1,now,Phases,out _,out _)==FireVerdict.Allowed){fired++;one.Spend(1);}}
            check(fired==1,"ammo: one real debit allows exactly one shot");
            // d: logging once per verdict and round
            var log=new ChamberLedger<int>();
            check(log.FirstTime(1,FireVerdict.NoRound)&&!log.FirstTime(1,FireVerdict.NoRound)&&log.FirstTime(1,FireVerdict.TooEarly),"ammo: a verdict is logged once");
            log.Debit(1,0);
            check(log.FirstTime(1,FireVerdict.NoRound),"ammo: a new load logs again");
            // e: the initial round of a stocked mix may fire, and a second one needs a debit
            var initial=new ChamberLedger<int>();initial.Initial(7,"heat");
            check(initial.Check(7,0,Phases,out _,out _)==FireVerdict.Allowed&&initial.TypeOf(7)=="heat","ammo: a round that was in the gun when the mix was stocked may fire");
            initial.Spend(7);
            check(initial.Check(7,1,Phases,out _,out _)==FireVerdict.NoRound,"ammo: ...once");
            // f: independent guns
            var two=new ChamberLedger<int>();two.Debit(1,0,"heat",instant:true);
            check(two.Check(1,0,Phases,out _,out _)==FireVerdict.Allowed&&two.Check(2,0,Phases,out _,out _)==FireVerdict.NoRound,"ammo: every load task has its own credit");
            two.Reset();check(two.Count==0&&two.Check(1,0,Phases,out _,out _)==FireVerdict.NoRound,"ammo: a reset (vehicle released) clears every credit");
        }

        // ---- the refund: once ----
        var latch=new RefundLatch();
        check(!latch.Used&&latch.TryTake()&&latch.Used&&!latch.TryTake()&&!latch.TryTake(),"ammo: the native refund of one occupied round is taken once, a repeat adds nothing");
        int refunded=0;var scope=new RefundLatch();
        void NativeRefund(){if(scope.TryTake())refunded++;}
        NativeRefund();NativeRefund(); // nested native calls (Unload inside Cancel) reach the same scope
        check(refunded==1,"ammo: a nested refund inside one scope still returns exactly one round");
        Console.WriteLine("0.22.7 ammunition rules PASS");
    }
}
