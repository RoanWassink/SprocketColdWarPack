using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using SprocketCarouselAutoloader;

// 0.2.23: static true-size props. The animation tests of 0.2.22 (phase, chain pitch, ratchet,
// door, feed lane, cycles/resync) are removed with the animation itself (BESLUITEN "Prop, geen
// simulatie"); the twin-drum tests are replaced by one drum ("Revolver = één trommel").
// 0.2.24: auto feed arm and measuring point, two-piece ammunition, configurable revolver limits.
// 0.2.25: revolver fire cycle 0.6 x bustle, refill from the bustle cycle (Timing).
internal static class VisualTests
{
    const double Mm = .001, Eps = 1e-5;
    static readonly JsonSerializerOptions Json = new() { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    internal static void Run(Action<bool,string> check)
    {
        var report=new List<string> {"Carousel Autoloader 0.2.25 geometry per spec (exact C# game mesh).",
            "id | kind | rack mm | round mm (cell) | native | capacity | layout | triangles (detail) | per round | arm beyond +z mm | feed distance old -> new m"};
        Directory.CreateDirectory("logs");
        foreach(var entry in VisualSpecs.Viewer.Concat(VisualSpecs.Extra).Concat(Sweep()))
            Spec(check,entry,report);
        Feed(check,report);
        TwoPiece(check);
        Timing(check);
        Mesh(check);
        Drum(check);
        Policy(check,report);
        Icon(check,report);
        File.WriteAllLines("logs/0.2.25-geometry.txt",report);
        Console.WriteLine("True-size geometry, auto feed arm, two-piece, drum capacity, stock display, mesh data and icon PASS.");
    }

    // Extra coverage beyond the viewer specs: sizes, calibres, mirror, multi-row, tiny racks, load
    // points around the rack and two-piece variants.
    static IEnumerable<VisualSpecs.Entry> Sweep()
    {
        var list=new List<VisualSpecs.Entry>();
        VisualSpecs.Entry E(string id,ModelKind kind,double x,double y,double z,double cal,double p,double c,bool mirror=false,bool two=false,(double,double,double)? load=null)
            => VisualSpecs.Make(id+(mirror?" mirror":"")+(two?" two-piece":"")+(load!=null?" load":""),id,kind,x,y,z,cal,p,c,"sweep",mirror,two,false,load);
        foreach(bool mirror in new[]{false,true})
        foreach(bool two in new[]{false,true})
        {
            var load=two?((double,double,double)?)(-.2,.1,1.4):null;
            list.Add(E("sweep bustle 2 rows",ModelKind.Bustle,.9,.3,2.1,90,270,700,mirror,two,load));
            list.Add(E("sweep bustle 1 column",ModelKind.Bustle,.16,.4,1.0,120,360,600,mirror,two,load));
            list.Add(E("sweep bustle 1 layer",ModelKind.Bustle,1.0,.13,1.0,120,360,600,mirror,two,load));
            list.Add(E("sweep bustle 125",ModelKind.Bustle,1.3,.52,1.5,125,375,600,mirror,two,load));
            list.Add(E("sweep bustle 20",ModelKind.Bustle,.2,.1,.4,20,60,120,mirror,two,load));
            list.Add(E("sweep revolver 2 rows",ModelKind.Revolver,.5,.5,1.4,60,180,420,mirror,two,load));
            list.Add(E("sweep revolver tall",ModelKind.Revolver,.4,.7,1.0,75,225,645,mirror,two,load));
            list.Add(E("sweep revolver 37",ModelKind.Revolver,.3,.3,.5,37,111,250,mirror,two,load));
            list.Add(E("sweep revolver single ring",ModelKind.Revolver,.27,.27,.95,75,225,645,mirror,two,load));
            list.Add(E("sweep revolver too small",ModelKind.Revolver,.15,.15,.95,75,225,645,mirror,two,load));
        }
        return list;
    }

    static (double X0,double X1,double Y0,double Y1,double Z0,double Z1) Box(MeshPart p)
    {
        double x0=1e9,x1=-1e9,y0=1e9,y1=-1e9,z0=1e9,z1=-1e9;
        for(int i=0;i<p.Vertices.Count;i+=3)
        {
            x0=Math.Min(x0,p.Vertices[i]);x1=Math.Max(x1,p.Vertices[i]);
            y0=Math.Min(y0,p.Vertices[i+1]);y1=Math.Max(y1,p.Vertices[i+1]);
            z0=Math.Min(z0,p.Vertices[i+2]);z1=Math.Max(z1,p.Vertices[i+2]);
        }
        return (x0,x1,y0,y1,z0,z1);
    }

    static double Dist(double x,double y,double z,(double X,double Y,double Z) p) => Math.Sqrt(Math.Pow(x-p.X,2)+Math.Pow(y-p.Y,2)+Math.Pow(z-p.Z,2));

    static void Spec(Action<bool,string> check,VisualSpecs.Entry entry,List<string> report)
    {
        var s=entry.Spec; string n=entry.Id;
        var model=AutoloaderModelGeometry.Create(s);
        check(JsonSerializer.Serialize(model,Json)==JsonSerializer.Serialize(AutoloaderModelGeometry.Create(s),Json),n+" deterministic creation");
        int dMm=(int)Math.Ceiling(s.Caliber),cellMm=FeedArmGeometry.CellMm(s),fullMm=(int)Math.Ceiling(entry.FullProjectile+entry.FullCharge);
        check(Math.Abs(model.DiameterM-dMm*Mm)<1e-9&&Math.Abs(model.LengthM-cellMm*Mm)<1e-9,n+" model uses the packer round (piece) size");
        int w=(int)Math.Round(s.X*1000),h=(int)Math.Round(s.Y*1000),l=(int)Math.Round(s.Z*1000);
        int native=RackPacking.NativeCapacity(w,h,l,dMm,fullMm);
        check(native==entry.NativeCapacity,n+" native capacity");
        int places=s.Kind==ModelKind.Bustle?RackPacking.NativeCapacity(w,h,l,dMm,cellMm):RevolverDrum.Capacity(w,h,l,dMm,cellMm);
        check(model.LayoutSlots==(s.TwoPiece?TwoPiecePolicy.Shots(places):places),n+" layout = packer grid or drum"+(s.TwoPiece?" / 2 (two-piece)":""));
        if(s.Kind==ModelKind.Bustle)
        {
            check(model.Columns*model.Layers*model.Rows==places,n+" bustle grid equals the native packer");
            check(s.TwoPiece||s.Capacity==native,n+" bustle without two-piece keeps the native capacity");
        }
        else
            check(model.Drum!.Capacity==places,n+" revolver layout equals the drum");
        // Feed arm: the model's arm is the shared placement, so what the player sees is what the game measures.
        var arm=FeedArmGeometry.For(s);
        check(arm==model.Arm||model.LayoutSlots==0,n+" model arm equals the gameplay feed placement");
        var rounds=model.Meshes.Where(p=>p.Role==PartRole.Round).OrderBy(p=>p.Slot).ToList();
        var outlet=BustleFeedGeometry.Outlet(s.X,s.Z,s.Caliber,s.Mirror);
        string feed="-";
        if(FeedArmGeometry.HasLoadPoint(s))
        {
            var load=(s.LoadX,s.LoadY,s.LoadZ);
            double old=Math.Min(Dist(outlet.X,outlet.Y,outlet.Z,load),Dist(-outlet.X,outlet.Y,outlet.Z,load)),now=arm.Distance(s.LoadX,s.LoadY,s.LoadZ);
            check(arm.Auto&&now<=old+1e-9,n+" new measuring point never farther from the load point than the old outlet");
            check(Math.Abs(arm.MeasureX)<=s.X/2+Eps&&Math.Abs(arm.MeasureY)<=s.Y/2+Eps&&Math.Abs(arm.MeasureZ-outlet.Z)<1e-9,n+" measuring point inside the rack plus the arm reach");
            feed=$"{Dist(outlet.X,outlet.Y,outlet.Z,load):0.000} -> {now:0.000}";
        }
        else check(!arm.Auto&&Math.Abs(arm.MeasureX-outlet.X)+Math.Abs(arm.MeasureY-outlet.Y)+Math.Abs(arm.MeasureZ-outlet.Z)<1e-12,n+" without a load point the 0.2.23 outlet is kept");
        if(s.Capacity==0)
        {
            check(model.Slots==0&&rounds.Count==0&&!model.Complete,n+" nothing fits: no own model, native grid kept");
            report.Add($"{n} | {s.Kind} | {w}x{h}x{l} | {dMm}x{cellMm} | {native} | 0 | no round fits | - | - | - | {feed}");
            return;
        }
        int perRound=s.TwoPiece?2:1;
        check(model.Complete&&model.Slots==s.Capacity&&rounds.Count==perRound*s.Capacity,n+" round places equal the capacity");
        check(rounds.GroupBy(p=>p.Slot).All(g=>g.Count()==perRound)&&rounds.Select(p=>p.Slot).Distinct().OrderBy(x=>x).SequenceEqual(Enumerable.Range(0,s.Capacity)),
            n+(s.TwoPiece?" one projectile and one charge per slot":" one round per slot 0..capacity-1"));
        if(!AutoloaderVisualPolicy.InBudget(model))
        {
            // Only dense sweep racks may exceed the budget; the adapter then keeps the native grid.
            check(entry.Source=="sweep"&&model.Triangles>AutoloaderVisualPolicy.TriangleBudget,n+" over budget only for dense sweep racks (native grid kept)");
            report.Add($"{n} | {s.Kind} | {w}x{h}x{l} | {dMm}x{cellMm} | {native} | {s.Capacity} | over budget ({model.Triangles}): native grid | - | - | - | {feed}");
            return;
        }
        check(model.Meshes.SelectMany(p=>p.Materials).All(m=>m>=0&&m<AutoloaderModelGeometry.MaterialCount),n+" five shared material slots");
        foreach(var part in model.Meshes)
        {
            check(part.Vertices.Count==part.Normals.Count&&part.Indices.Count==part.Materials.Count*3,n+" "+part.Name+" attribute counts");
            check(part.Vertices.All(float.IsFinite)&&part.Normals.All(float.IsFinite),n+" "+part.Name+" finite mesh");
            check(part.Indices.All(i=>i>=0&&i<part.Vertices.Count/3),n+" "+part.Name+" indices in range");
        }
        // True size: diameter of the packer; length of the round, or each piece its own length (+-1 mm).
        foreach(var r in rounds)
        {
            var b=Box(r);
            bool projectile=r.Name.StartsWith("Projectiel");
            double expected=!s.TwoPiece?fullMm*Mm:projectile?s.Projectile*Mm:s.Charge*Mm;
            check(Math.Abs(b.X1-b.X0-dMm*Mm)<=Mm&&Math.Abs(b.Y1-b.Y0-dMm*Mm)<=Mm,n+" "+r.Name+" has the packer diameter");
            check(Math.Abs(b.Z1-b.Z0-expected)<=Mm,n+" "+r.Name+" has its true length");
            check(r.Triangles<=170,n+" "+r.Name+" at most about 160 triangles");
        }
        if(s.TwoPiece) check(rounds.Count(r=>r.Name.StartsWith("Projectiel"))==s.Capacity&&rounds.Count(r=>r.Name.StartsWith("Lading"))==s.Capacity,n+" as many projectiles as charges");
        // Rounds never overlap.
        var axes=rounds.Select(r=>{var b=Box(r);return ((b.X0+b.X1)/2,(b.Y0+b.Y1)/2,b.Z0,b.Z1);}).ToList();
        double gap=s.Kind==ModelKind.Revolver?RevolverDrum.ClearanceMm*Mm:0;
        bool apart=true;
        for(int i=0;i<axes.Count;i++) for(int j=i+1;j<axes.Count;j++)
            if(axes[i].Item3<axes[j].Item4-Eps&&axes[j].Item3<axes[i].Item4-Eps)
                apart&=Math.Sqrt(Math.Pow(axes[i].Item1-axes[j].Item1,2)+Math.Pow(axes[i].Item2-axes[j].Item2,2))>=dMm*Mm+gap-1e-6;
        check(apart,n+" rounds do not overlap (revolver: with chamber clearance)");
        // Bounds: everything inside, except the +z end of the feed arm.
        double X=s.X/2+Eps,Y=s.Y/2+Eps,Z=s.Z/2+Eps,protrusion=0;
        foreach(var part in model.Meshes)
        {
            var b=Box(part);
            bool lateral=b.X0>=-X&&b.X1<=X&&b.Y0>=-Y&&b.Y1<=Y&&b.Z0>=-Z;
            if(part.Role==PartRole.Arm) { check(lateral,n+" arm "+part.Name+" only protrudes toward +z"); protrusion=Math.Max(protrusion,b.Z1-s.Z/2); }
            else check(lateral&&b.Z1<=Z,n+" "+part.Name+" inside the bounds");
        }
        check(Math.Abs(Math.Max(0,protrusion)-model.ArmProtrusionM)<1e-6,n+" reported arm protrusion");
        check(protrusion<=outlet.Z-s.Z/2+Eps,n+" arm ends at the gameplay outlet");
        // A clearly visible arm: trough about 1.1 d wide, no part thinner than 0.3 d; the measuring point is on its front face.
        var armParts=model.Meshes.Where(p=>p.Role==PartRole.Arm).ToList();
        var trough=armParts.Single(p=>p.Name=="Laadgoot"); var tb=Box(trough);
        check(Math.Abs(tb.X1-tb.X0-1.1*dMm*Mm)<.5*Mm,n+" feed trough is 1.1 d wide");
        check(arm.MeasureX>=tb.X0-1e-4&&arm.MeasureX<=tb.X1+1e-4&&arm.MeasureY>=tb.Y0-1e-4&&arm.MeasureY<=Math.Min(arm.Y+.5*dMm*Mm,s.Y/2)+1e-4&&Math.Abs(arm.MeasureZ-tb.Z1)<1e-4||!arm.Auto,
            n+" measuring point lies on the arm's front face");
        foreach(var part in armParts)
        {
            var b=Box(part);
            check(Math.Min(b.X1-b.X0,Math.Min(b.Y1-b.Y0,b.Z1-b.Z0))>=.3*dMm*Mm-1e-6,n+" "+part.Name+" at least 0.3 d thick");
        }
        // Layout and outlet-first order (the first place gets the projectile of the first round to go).
        var firstPart=rounds.Where(p=>p.Slot==s.Capacity-1).OrderBy(p=>p.Name.StartsWith("Projectiel")?0:1).First();
        var first=Box(firstPart);
        if(s.Kind==ModelKind.Bustle)
        {
            int sgn=arm.Auto?(arm.X>=0?1:-1):(s.Mirror?-1:1);
            double x0=-model.Columns*dMm*Mm/2;
            foreach(var a in axes)
            {
                double col=(a.Item1-x0)/(dMm*Mm)-.5;
                check(Math.Abs(col-Math.Round(col))<1e-4&&Math.Round(col)>=0&&Math.Round(col)<model.Columns,n+" round on a packer column");
            }
            check(axes.All(a=>sgn*((first.X0+first.X1)/2)>=sgn*a.Item1-1e-6),n+" first round to vanish is in the column on the arm side");
            check(axes.All(a=>(first.Y0+first.Y1)/2>=a.Item2-1e-6)&&axes.All(a=>first.Z1>=a.Item4-1e-6),n+" first round to vanish is top layer, front row");
        }
        else
        {
            var drum=model.Drum!;
            double D=Math.Min(s.X,s.Y),cx=arm.DrumX,cy=arm.DrumY;
            check(Math.Abs(cx)<=(s.X-D)/2+1e-9&&Math.Abs(cy)<=(s.Y-D)/2+1e-9,n+" drum inside the rack");
            var radii=drum.Rings.Select(r=>r.RadiusMm*Mm).ToArray();
            foreach(var a in axes)
            {
                double r=Math.Sqrt(Math.Pow(a.Item1-cx,2)+Math.Pow(a.Item2-cy,2));
                check(radii.Any(q=>Math.Abs(q-r)<1e-5),n+" round on a drum ring");
                check(r+dMm*Mm/2<=D/2-RevolverDrum.WallMm*Mm+1e-6&&r-dMm*Mm/2>=drum.HubMm*Mm-1e-6,n+" chamber between wall and hub");
            }
            double fx=(first.X0+first.X1)/2,fy=(first.Y0+first.Y1)/2;
            check(Math.Abs(fx-(cx+radii[0]*Math.Cos(arm.Angle)))<1e-5&&Math.Abs(fy-(cy+radii[0]*Math.Sin(arm.Angle)))<1e-5&&first.Z1>=axes.Max(a=>a.Item4)-1e-6,
                n+" first round to vanish is in the ram chamber on the outer ring, front");
            check(drum.Chambers*drum.Rows==places,n+" chambers x rows");
        }
        // Hiding per stock level: exactly AmountStored complete rounds (two-piece: both pieces), ram side first;
        // the native fallback shows none. The adapter's prefix buffers match.
        for(int stored=0;stored<=s.Capacity;stored++)
        {
            var visible=rounds.Where(p=>AutoloaderVisualPolicy.Visible(p,stored)).Select(p=>p.Slot).ToList();
            check(visible.Distinct().OrderBy(x=>x).SequenceEqual(Enumerable.Range(0,stored))&&visible.Count==perRound*stored,n+" visible rounds equal AmountStored "+stored);
            check(!rounds.Any(p=>AutoloaderVisualPolicy.Visible(p,stored,false)),n+" native fallback shows no own rounds");
            check(model.Meshes.Where(p=>p.Role!=PartRole.Round).All(p=>AutoloaderVisualPolicy.Visible(p,stored)),n+" frame always visible");
        }
        check(AutoloaderVisualPolicy.Stock(s.Capacity,s.Capacity+5)==s.Capacity&&AutoloaderVisualPolicy.Stock(s.Capacity,-2)==0,n+" stored rounds clamp to the capacity");
        for(int m=0;m<AutoloaderModelGeometry.MaterialCount;m++)
        {
            var prefix=AutoloaderVisualPolicy.Prefix(rounds,m,s.Capacity);
            bool ok=prefix[0]==0;
            for(int stored=1;stored<=s.Capacity;stored++)
                ok&=prefix[stored]==rounds.Where(p=>p.Slot<stored).Sum(p=>p.Materials.Count(x=>x==m));
            check(ok,n+" prefix buffer of material "+m+" matches every stock level");
        }
        string layout=s.Kind==ModelKind.Bustle?$"grid {model.Columns}x{model.Layers}x{model.Rows}":$"drum {string.Join("+",model.Drum!.Rings.Select(r=>r.Chambers))} x {model.Rows}";
        if(s.TwoPiece) layout+=$", {model.Pieces} pieces";
        report.Add($"{n} | {s.Kind} | {w}x{h}x{l} | {dMm}x{cellMm} | {native} | {s.Capacity} | {layout} | {model.Triangles} ({model.Detail}) | {rounds[0].Triangles} | {model.ArmProtrusionM*1000:0} | {feed}");
    }

    // A: synthetic racks beside, in front of and behind a load point; the T-84 estimate.
    static void Feed(Action<bool,string> check,List<string> report)
    {
        int count=0;
        foreach(var kind in new[]{ModelKind.Bustle,ModelKind.Revolver})
        foreach(bool mirror in new[]{false,true})
        foreach(double lx in new[]{-2.0,-.6,-.2,0,.15,.55,2})
        foreach(double ly in new[]{-.4,0,.3})
        foreach(double lz in new[]{-1.5,0,.6,.9,1.6,3})
        {
            var e=kind==ModelKind.Bustle
                ? VisualSpecs.Make("synthetic","synthetic bustle",kind,1.2,.4,1.2,120,360,620,"test",mirror,false,false,(lx,ly,lz))
                : VisualSpecs.Make("synthetic","synthetic revolver",kind,.6,.5,1.1,90,270,700,"test",mirror,false,false,(lx,ly,lz));
            var s=e.Spec; var arm=FeedArmGeometry.For(s);
            var o=BustleFeedGeometry.Outlet(s.X,s.Z,s.Caliber,mirror);
            double old=Dist(o.X,o.Y,o.Z,(lx,ly,lz)),now=arm.Distance(lx,ly,lz);
            check(now<=old+1e-9,$"synthetic {kind} load ({lx},{ly},{lz}): new point never farther than the old outlet");
            check(Math.Abs(arm.MeasureX)<=s.X/2+Eps&&Math.Abs(arm.MeasureY)<=s.Y/2+Eps&&Math.Abs(arm.MeasureZ-o.Z)<1e-9,$"synthetic {kind}: measuring point within the rack plus the arm reach");
            if(s.Capacity>0) check(AutoloaderModelGeometry.Create(s).Arm==arm,$"synthetic {kind}: model arm at the measuring placement");
            count++;
        }
        var t84=VisualSpecs.Make("t84","T-84",ModelKind.Bustle,1.361,.310,1.665,155,465,1200,"blueprint",load:VisualSpecs.T84Load).Spec;
        var t=FeedArmGeometry.For(t84);
        double before=Dist(VisualSpecs.T84OldOutlet.X,VisualSpecs.T84OldOutlet.Y,VisualSpecs.T84OldOutlet.Z,VisualSpecs.T84Load),after=t.Distance(t84.LoadX,t84.LoadY,t84.LoadZ);
        check(Math.Abs(before-1.13)<.005&&after<before,"T-84 estimate: old 1.13 m, new measuring point closer");
        report.Add($"Feed: {count} synthetic placements never farther than the old outlet. T-84 (load point estimated on the gun axis): old {before:0.000} m -> new {after:0.000} m, arm at x {t.X*1000:0} mm, y {t.Y*1000:0} mm.");
    }

    // B/E3: two-piece pieces, capacity, cycle, and the capacity table.
    static void TwoPiece(Action<bool,string> check)
    {
        check(TwoPiecePolicy.Pieces(0,375,713,125)==(680,408),"125 mm native round splits 680 + 408 mm (carousel)");
        var api=TwoPiecePolicy.Pieces(1,1023,1200,155);
        check(api==(1023,1200)&&TwoPiecePolicy.LongestMm(api.Projectile,api.Charge)==1200,"shell API pieces are used as reported (T-84 gen4: 1023 + 1200)");
        check(TwoPiecePolicy.Shots(16)==8&&TwoPiecePolicy.Shots(17)==8&&TwoPiecePolicy.Shots(1)==0&&TwoPiecePolicy.Shots(-3)==0,"complete rounds = floor(pieces / 2)");
        check(Math.Abs(TwoPiecePolicy.CycleSeconds(4)-5)<1e-12,"two-piece cycle is 1.25x");
        check(Math.Abs(TwoPiecePolicy.CycleSeconds(RevolverPolicy.CycleSeconds(4))-3)<1e-12,"two-piece revolver cycle is 1.25 x 0.6 x bustle");
        // Without the checkbox nothing changes: the spec keeps the complete round and the native grid.
        var plain=VisualSpecs.Make("p","plain",ModelKind.Bustle,1.1,.4,1.62,120,360,620,"test").Spec;
        var model=AutoloaderModelGeometry.Create(plain);
        check(!plain.TwoPiece&&plain.Projectile==360&&plain.Charge==620&&model.Pieces==0&&model.LayoutSlots==27,"without two-piece the bustle is unchanged");
        var table=new List<string>{"Carousel Autoloader 0.2.25 two-piece capacity (pieces = floor(W/d) x floor(H/d) x floor(L/longest), complete rounds = floor(pieces / 2)).",
            "rack W x H x L mm | calibre | pieces projectile + charge (longest) mm | complete-round capacity | two-piece capacity | revolver drum two-piece"};
        foreach(var (w,h,l,cal,p,c,status,name) in new (int,int,int,int,double,double,int,string)[]{
            (1361,310,1665,155,1023,1200,1,"T-84 gen4 (API)"),(1361,310,1665,155,465,1200,0,"T-84 native"),
            (1400,500,1050,105,315,650,0,"105 mm"),(1100,400,1620,120,360,620,0,"120 mm"),(1100,400,900,120,360,620,0,"120 mm short rack"),
            (1300,400,1500,125,375,713,0,"125 mm"),(1300,400,750,125,375,713,0,"125 mm short rack"),(1300,400,650,125,375,713,0,"125 mm too short"),
            (560,560,1250,105,693,1200,1,"105 mm 693+1200 (API)")})
        {
            int full=(int)Math.Ceiling(p+c);
            var pieces=TwoPiecePolicy.Pieces(status,p,c,cal);
            int longest=TwoPiecePolicy.LongestMm(pieces.Projectile,pieces.Charge);
            int complete=RackPacking.NativeCapacity(w,h,l,cal,full),packed=RackPacking.NativeCapacity(w,h,l,cal,longest),shots=TwoPiecePolicy.Shots(packed);
            int drum=TwoPiecePolicy.Shots(Math.Min(packed,RevolverDrum.Capacity(w,h,l,cal,longest)));
            check(shots==packed/2&&longest<full,name+": two-piece needs only the longest piece");
            if(l<full) check(complete==0&&(l<longest?shots==0:shots>0),name+": a rack shorter than the round holds pieces only");
            table.Add($"{name}: {w} x {h} x {l} | {cal} | {pieces.Projectile:0} + {pieces.Charge:0} ({longest}) | {complete} | {shots} ({packed} pieces) | {drum}");
        }
        File.WriteAllLines("logs/0.2.25-twopiece-capacity.txt",table);
    }

    // 0.2.25: timing table for the T-84 round and two smaller calibres. The shell mass is not an input of the
    // tests; ASSUMPTION: it scales with calibre^2 x length from the 125 mm reference the tests already use
    // (45.8 kg at 125 mm x 1341 mm), which only moves the cycle through the small mass term of BustleTiming.
    static void Timing(Action<bool,string> check)
    {
        const double RefMass=45.8,RefCal=125,RefLen=1341;
        double Mass(double cal,double len)=>RefMass*(cal/RefCal)*(cal/RefCal)*(len/RefLen);
        var table=new List<string>{"Carousel Autoloader 0.2.25 revolver timing (seconds). bustle = BustleTiming.ReloadSeconds(calibre, mass, full round length, 0).",
            $"Assumed shell mass = {RefMass} kg x (calibre / {RefCal})^2 x (length / {RefLen}); defaults CycleFactor {RevolverPolicy.CycleFactor}, RefillCycleFactor {RevolverPolicy.RefillCycleFactor}, MinimumRefillSeconds {RevolverPolicy.MinimumRefillSeconds}; two-piece x {TwoPiecePolicy.CycleFactor}.",
            "round | calibre mm | length mm | mass kg | bustle | revolver (0.6) | bustle two-piece | revolver two-piece | refill per round | refill per round two-piece | old revolver (1.5) | old refill (from the revolver cycle)"};
        foreach(var (name,cal,len,mass) in new (string,double,double,double)[]{
            ("T-84 (1023 + 1200 mm)",155,1023+1200,Mass(155,1023+1200)),("T-84 at the flat test mass",155,1023+1200,RefMass),
            ("105 mm (693 + 1200 mm)",105,693+1200,Mass(105,693+1200)),("105 mm native (315 + 785 mm)",105,315+785,Mass(105,315+785)),
            ("75 mm AMX-13 (225 + 645 mm)",75,225+645,Mass(75,225+645))})
        {
            var bustle=BustleTiming.ReloadSeconds(cal,mass,len,0);
            var revolver=RevolverPolicy.FireCycleSeconds(bustle,true,false);
            var bustleTwo=RevolverPolicy.FireCycleSeconds(bustle,false,true);
            var revolverTwo=RevolverPolicy.FireCycleSeconds(bustle,true,true);
            var refill=RevolverPolicy.RefillSeconds(bustle,false);
            var refillTwo=RevolverPolicy.RefillSeconds(bustle,true);
            check(Math.Abs(revolver-.6*bustle)<1e-12&&Math.Abs(revolverTwo-1.25*.6*bustle)<1e-12&&Math.Abs(bustleTwo-1.25*bustle)<1e-12,name+": revolver 0.6 x bustle, two-piece x 1.25 on both");
            check(Math.Abs(refill-Math.Max(10,3*bustle))<1e-12&&Math.Abs(refillTwo-Math.Max(10,3*1.25*bustle))<1e-12,name+": refill = max(10 s, 3 x bustle cycle), two-piece included");
            check(refill>=10&&refill>=3*bustle&&refillTwo>=3*bustleTwo&&refill>=revolver,name+": refill is at least 10 s, 3 x the bustle cycle and never faster than firing");
            var oldRefill=Math.Max(10,3*1.5*bustle);
            table.Add($"{name} | {cal} | {len} | {mass:0.0} | {bustle:0.00} | {revolver:0.00} | {bustleTwo:0.00} | {revolverTwo:0.00} | {refill:0.0} | {refillTwo:0.0} | {1.5*bustle:0.00} | {oldRefill:0.0}");
        }
        File.WriteAllLines("logs/0.2.25-timing.txt",table);
    }

    static void Mesh(Action<bool,string> check)
    {
        // Embedded OpenSCAD data belongs to the current export (Models/scad/build-parts.ps1 copies the STLs).
        string models=Path.Combine(AppContext.BaseDirectory,"..","..","..","..","Models");
        foreach(var unit in AutoloaderMeshData.All)
        {
            var path=Path.Combine(models,unit.Name+".stl");
            check(File.Exists(path),unit.Name+" STL export present");
            var bytes=File.ReadAllBytes(path);
            check(Convert.ToHexString(SHA256.HashData(bytes))==unit.StlSha256,unit.Name+" embedded data matches the STL export (SHA256)");
            int facets=System.Text.Encoding.ASCII.GetString(bytes).Split('\n').Count(line=>line.TrimStart().StartsWith("facet normal"));
            check(facets==unit.StlFacets&&unit.Triangles<=facets&&unit.Triangles>0,unit.Name+" facet count");
            check(unit.Indices.All(i=>i>=0&&i<unit.Positions.Length/3),unit.Name+" indices in range");
        }
        var round=AutoloaderMeshData.Round12;
        double maxR=0,maxZ=0;
        for(int i=0;i<round.Positions.Length;i+=3) { maxR=Math.Max(maxR,Math.Max(Math.Abs(round.Positions[i]),Math.Abs(round.Positions[i+1]))); maxZ=Math.Max(maxZ,round.Positions[i+2]); }
        check(Math.Abs(maxR-.5)<1e-6&&Math.Abs(maxZ-(AutoloaderModelGeometry.CaseZone+AutoloaderModelGeometry.ProjectileZone))<1e-6,"OpenSCAD round matches the C# zone constants");
        // No moving parts: the animation API and per-part transforms are gone.
        check(typeof(AutoloaderModelGeometry).GetMethod("ApplyPhase")==null&&typeof(AutoloaderModelGeometry).GetMethod("LoopPoint")==null,"no animation API");
        check(new[]{"Motion","Position","Angle","Scale","Base"}.All(p=>typeof(MeshPart).GetProperty(p)==null),"mesh parts carry no transform or motion");
        check(new[]{"Cycles","Resync","Loading"}.All(p=>typeof(AutoloaderVisualPolicy).GetMethod(p)==null),"visual policy has no load phase");
    }

    static void Drum(Action<bool,string> check)
    {
        // Known layouts (hand-checked against the brief's formulas).
        var a=RevolverDrum.Plan(420,420,920,75,870);
        check(a.Rings.Select(r=>r.Chambers).SequenceEqual(new[]{12,6})&&a.Rows==1&&Math.Abs(a.Rings[0].RadiusMm-162.5)<1e-9&&Math.Abs(a.Rings[1].RadiusMm-82.5)<1e-9,"75 mm drum 420 mm: 12 + 6 chambers");
        check(RevolverDrum.Plan(560,560,1150,105,1100).Rings.Select(r=>r.Chambers).SequenceEqual(new[]{12,5}),"105 mm drum 560 mm: 12 + 5 chambers");
        check(RevolverDrum.Plan(900,450,950,75,870).Rings.Select(r=>r.Chambers).SequenceEqual(new[]{13,7}),"75 mm drum min(900,450): 13 + 7 chambers");
        check(RevolverDrum.Capacity(500,500,1400,60,600)==RevolverDrum.Plan(500,500,1400,60,600).Capacity&&RevolverDrum.Rows(1400,600)==2,"two rows per chamber");
        check(RevolverDrum.HubRadiusMm(75)==30&&RevolverDrum.HubRadiusMm(200)==40,"hub max(30 mm, 0.2 d)");
        check(RevolverDrum.Capacity(150,150,950,75,870)==0&&RevolverDrum.Capacity(420,420,899,75,870)==0,"0 when no chamber or no row fits");
        foreach(var bad in new[]{double.NaN,0,-1,double.PositiveInfinity})
            check(RevolverDrum.Capacity(bad,400,900,75,800)==0&&RevolverDrum.Capacity(400,400,900,bad,800)==0&&RevolverDrum.Capacity(400,400,900,75,bad)==0,"invalid drum input gives 0");
        check(RevolverDrum.Limit(-4,420,420,920,75,870)==0&&RevolverDrum.Limit(10,420,420,920,75,870)==10&&RevolverDrum.Limit(99,420,420,920,75,870)==18,"limit = min(native, drum), never negative");
        // Capacity table: more rounds in a bigger rack, never more than native, 0 if it does not fit.
        var table=new List<string>{"Carousel Autoloader 0.2.23 revolver drum capacity (wall 10 mm, clearance 5 mm, hub max(30 mm, 0.2 d), end plates 15 mm).",
            "Square rack W = H = drum diameter; length = round length + 30 mm (1 row) and 2 x round length + 30 mm (2 rows).",
            "Per entry: drum diameter mm: chambers per ring -> drum capacity (native packer capacity).",""};
        foreach(var (d,len) in new[]{(20,150),(37,300),(50,500),(75,870),(90,950),(105,1100)})
        {
            int previous=-1;
            foreach(int rows in new[]{1,2})
            {
                int length=rows*len+30; previous=-1;
                var line=new System.Text.StringBuilder($"{d} mm / {len} mm round, rack {length} mm long ({rows} row{(rows==1?"":"s")}):");
                for(int size=100;size<=1000;size+=50)
                {
                    int cap=RevolverDrum.Capacity(size,size,length,d,len),native=RackPacking.NativeCapacity(size,size,length,d,len);
                    check(cap>=previous,$"{d} mm drum capacity grows with the rack ({size} mm)");
                    check(cap<=native,$"{d} mm drum never holds more than native ({size} mm)");
                    check(RevolverDrum.Capacity(size,size+200,length,d,len)==cap&&RevolverDrum.Capacity(size,size,length+len-1,d,len)==cap,$"{d} mm drum uses min(W,H) and whole rows");
                    check(cap==0||RevolverDrum.Capacity(size,size,length,d,len)%rows==0,$"{d} mm capacity is chambers x rows");
                    previous=cap;
                    if(size%100==0||size==150)
                    {
                        var plan=RevolverDrum.Plan(size,size,length,d,len);
                        line.Append($"  {size}: {(plan.Rings.Count==0?"-":string.Join("+",plan.Rings.Select(r=>r.Chambers)))} -> {cap} ({native})");
                    }
                }
                check(RevolverDrum.Capacity(100,100,length,d,len)<=RevolverDrum.Capacity(1000,1000,length,d,len),$"{d} mm larger drum holds at least as many");
                table.Add(line.ToString());
            }
            check(RevolverDrum.Capacity(1000,1000,len+29,d,len)==0,$"{d} mm round longer than the drum space gives 0");
        }
        File.WriteAllLines("logs/0.2.23-drum-capacity.txt",table);
    }

    static void Policy(Action<bool,string> check,List<string> report)
    {
        check(!AutoloaderVisualPolicy.Supported(0)&&AutoloaderVisualPolicy.Supported(1)&&AutoloaderVisualPolicy.Supported(192)&&!AutoloaderVisualPolicy.Supported(193),"capacity pre-check 1..192 (one drum needs one chamber)");
        // The pre-check never rejects a model that would fit the budget: sweep the densest racks.
        int maxBustle=0,maxRevolver=0;
        foreach(var (w,cal) in new[]{(100,20),(150,20),(200,20),(300,20),(500,20),(700,20),(900,20)})
        {
            var bustle=new Spec("dense",ModelKind.Bustle,w/1000.0,.2,.4,cal,60,100,0);
            int cap=RackPacking.NativeCapacity(w,200,400,cal,160);
            var m=AutoloaderModelGeometry.Create(bustle with {Capacity=cap});
            if(AutoloaderVisualPolicy.InBudget(m)) maxBustle=Math.Max(maxBustle,cap);
            int drum=RevolverDrum.Capacity(w,w,200,cal,160);
            var r=AutoloaderModelGeometry.Create(new Spec("dense",ModelKind.Revolver,w/1000.0,w/1000.0,.2,cal,60,100,drum));
            if(drum>0&&AutoloaderVisualPolicy.InBudget(r)) maxRevolver=Math.Max(maxRevolver,drum);
        }
        check(maxBustle<AutoloaderVisualPolicy.MaximumCapacity&&maxRevolver<AutoloaderVisualPolicy.MaximumCapacity,"capacity pre-check never rejects an in-budget model");
        report.Add($"Densest sweep accepted within 12000 triangles: bustle {maxBustle}, revolver {maxRevolver} rounds; larger racks keep the native grid.");
    }

    static void Icon(Action<bool,string> check,List<string> report)
    {
        var rgba=RevolverIcon.Pixels();
        var fixture=Png.Read(Path.Combine(AppContext.BaseDirectory,"Fixtures","revolver-approved.png"));
        check(rgba.SequenceEqual(fixture),"Revolver C# icon is pixel-identical to the single-drum fixture");
        Directory.CreateDirectory("logs");
        Png.Write("logs/revolver-icon-check.png",rgba);
        check(Png.Read("logs/revolver-icon-check.png").SequenceEqual(rgba),"PNG dump roundtrip");
        // One drum: a single steel disc around the centre (no second drum at the old x=91).
        int Px(int x,int y,int c)=>rgba[(y*128+x)*4+c];
        check(Px(62,64-30,0)==82&&Px(62,64+30,0)==82&&Px(91,40,0)!=0,"icon shows one drum disc");
        report.Add("Revolver icon RGBA SHA256: "+Convert.ToHexString(SHA256.HashData(rgba)));
    }
}
