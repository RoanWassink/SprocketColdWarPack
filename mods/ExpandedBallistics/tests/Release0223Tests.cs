namespace SprocketExpandedBallistics;

/// <summary>0.22.3: composer, shell-editor list, combat report view and ammunition mix/HUD rules.</summary>
internal static class Release0223Tests
{
    internal static void Run(Action<bool,string> check)
    {
        ComposerUiTests.Run(check);
        ShellList(check);CombatReport(check);Ammo(check);
        Console.WriteLine("0.22.3 shell list, combat report and ammunition checks PASS");
    }

    private static void ShellList(Action<bool,string> check)
    {
        var modules=new Dictionary<string,string>{["tail"]="fins",["propulsion"]="ballistic",["body"]="longRod",["effect"]="kinetic",["carrier"]="sabot"};
        check(ShellEditorText.ShortChain(modules)=="Fins · Rod","APFSDS subtitle is short");
        modules=new(){["tail"]="guided",["propulsion"]="rocket",["body"]="shapedCharge",["effect"]="heat",["carrier"]="gunLaunch"};
        check(ShellEditorText.ShortChain(modules)=="Guided · Rocket · HEAT · Gun-launched","gun-launched ATGM subtitle");
        check(ShellEditorText.AvailabilityTag("From 1985")=="1985+"&&ShellEditorText.AvailabilityTag("Retired 1990")=="RETIRED 1990"&&ShellEditorText.AvailabilityTag("Launcher only")=="LAUNCHER"
            &&ShellEditorText.AvailabilityTag("Unavailable")=="N/A"&&ShellEditorText.AvailabilityTag("Archive (retired 1990)")=="ARCHIVE"&&ShellEditorText.AvailabilityTag("")==null,"availability reasons map to chips by prefix");
        check(ShellEditorText.Dimmed(false,"Retired 1990")&&!ShellEditorText.Dimmed(false,"Archive (retired 1990)")&&!ShellEditorText.Dimmed(true,"From 1945"),"archive is selectable and not dimmed");
        check(ShellEditorText.IsYesNo(new[]{"no","yes"})&&!ShellEditorText.IsYesNo(new[]{"sight","none"})&&ShellEditorText.Caption("keepAfterRetirement")=="Keep in later eras (archive)","no/yes fields become toggles");

        var dir=Path.Combine(SprocketExpandedBallistics.Fix02228Tests.FixtureRoot(),"eb-0223-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            var runtime=Path.Combine(dir,"shells.json");File.WriteAllText(runtime,ReleaseProfiles.Defaults());
            var m=ShellEditorModel.Open(runtime,Path.Combine(dir,"modules.json"),Path.Combine(dir,"import.json"),false,null,()=>0);
            var asked=0;
            m.Availability=id=>{asked++;return id switch
            {
                "apfsds_long"=>new ShellAvailability(false,"From 1985",new DateTime(1985,1,1)),
                "he"=>new ShellAvailability(true,"Archive (retired 1990)",new DateTime(1950,1,1)),
                "hesh"=>new ShellAvailability(false,"Retired 1970",new DateTime(1946,1,1)),
                _=>new ShellAvailability(true,"From 1945",new DateTime(1945,9,3))
            };};
            var rows=m.Rows;var rod=rows.Single(r=>r.Id=="apfsds_long");var he=rows.Single(r=>r.Id=="he");var hesh=rows.Single(r=>r.Id=="hesh");
            check(rod.Unavailable&&rod.Tag=="1985+"&&rod.Subtitle=="Fins · Rod · From 1985"&&rod.Chain.Contains("Long rod"),"unavailable shell: chip, date in the subtitle, full chain kept");
            check(!he.Unavailable&&he.Tag=="ARCHIVE"&&hesh.Unavailable&&hesh.Tag=="RETIRED 1970","archive and retired rows");
            var count=asked;_=m.Rows;m.Select(1);_=m.Rows;
            check(asked==count,"availability asked once per shell, not per frame or version");
            check(m.Filtered("").Count==rows.Count&&ReferenceEquals(m.Filtered(""),m.Rows),"list order by default");
            m.SortByDate=true;var sorted=m.Filtered("");
            check(sorted[^1].Id=="apfsds_long"&&sorted.Count==rows.Count&&m.IdAt(0)==rows[0].Id&&m.IsSaved(m.IndexOf("apfsds_long")),"sort by date is display-only");
            check(m.BehaviourLine.Length>0,"header line");
        }
        finally{Directory.Delete(dir,true);}
    }

    private static void CombatReport(Action<bool,string> check)
    {
        var shots=new List<CombatReportShot>();
        for(int n=1;n<=12;n++)
        {
            var layer=new CombatLayerInfo("a"+n,new[]{new CombatLayerPart(7,"Plate #7","rha","Steel (RHA)",80,30,true)},false);
            shots.Add(new CombatReportShot(n,n%2==0?"Outgoing":"Incoming","HEAT 105 mm",700,new[]
            {
                new CombatReportEvent("layerStart","","Reached layer",400,null,"a"+n,layer),
                new CombatReportEvent("plateResidual","","Traversed Plate #7",400,n==12?0:250,"a"+n),
                new CombatReportEvent("interaction","","Crew member Gunner killed")
            },0,400));
        }
        var view=CombatReportView.Build(shots);
        check(view.Count==10&&view[0].Block.Number==12&&view[9].Block.Number==3,"last ten impacts, newest first");
        var row=view[1].Layers.Single();
        check(row.Material=="Steel (RHA)"&&row.Thickness=="80 mm"&&row.Angle=="30°"&&row.BeforeAfter=="400 → 250"&&row.Reaction==""&&row.Note==""&&view[1].SummaryLine=="1 crew killed (gunner)"&&row.Colour=="#79D8E8","layer table: material, thickness, angle, before→after, no reaction for plain steel, hit colour; the damage is the summary line (0.22.7)");
        check(view[1].Block.Result==HitBlockBuilder.Penetrated&&view[1].Summary.EndsWith("1 layer",StringComparison.Ordinal),"same result word as the hit panel");
        check(CombatReportView.Build(Array.Empty<CombatReportShot>()).Count==0,"empty report");
    }

    private static void Ammo(Action<bool,string> check)
    {
        var first=AmmoInspectorPolicy.Add(Array.Empty<AmmoLoadoutEntry>(),"apds_late","heat",24);
        check(first.Count==2&&first[0]==new AmmoLoadoutEntry("apds_late",24)&&first[1]==new AmmoLoadoutEntry("heat",0),"one click: native selection with its rounds plus the new type");
        check(AmmoInspectorPolicy.Add(Array.Empty<AmmoLoadoutEntry>(),null,"heat",20).Select(e=>e.ProfileId).SequenceEqual(new[]{ShellProfiles.Vanilla,"heat"}),"no saved selection: the vanilla shell is row one");
        check(AmmoInspectorPolicy.Add(Array.Empty<AmmoLoadoutEntry>(),"heat","heat",20).Single()==new AmmoLoadoutEntry("heat",20),"adding the native type gives one row");
        check(AmmoInspectorPolicy.Add(first,"apds_late","he",24).Count==3&&ReferenceEquals(AmmoInspectorPolicy.Add(first,"apds_late","heat",24),first),"later adds append; duplicates ignored");

        var inactive=new[]{new AmmoLoadoutEntry("heat",0)};
        var r=AmmoInspectorPolicy.Reconcile(inactive,"apds_late");
        check(r.Entries.Single()==new AmmoLoadoutEntry("apds_late",0)&&r.Selection=="apds_late","native change with an inactive one-type mix: the row follows");
        var active=new[]{new AmmoLoadoutEntry("heat",9)};
        r=AmmoInspectorPolicy.Reconcile(active,"apds_late");
        check(ReferenceEquals(r.Entries,active)&&r.Selection=="heat","a mix with rounds overrides the native selection (heat=9 vs apds_late)");
        var two=new[]{new AmmoLoadoutEntry("heat",9),new AmmoLoadoutEntry("apds_late",3)};
        check(AmmoInspectorPolicy.Reconcile(two,"apds_late").Selection=="apds_late","a carried selection stays");
        check(AmmoInspectorPolicy.Disallowed(two,id=>id!="apds_late").Single()=="apds_late"&&AmmoInspectorPolicy.Disallowed(new[]{new AmmoLoadoutEntry("apds_late",0)},_=>false).Count==0,"types the vehicle date no longer allows are named (only with rounds)");

        var native=AmmoHudFormat.Native("APDS late","APDS",23,AmmoLoadState.Loaded);
        check(native.Name=="APDS late"&&native.Rounds=="× 23"&&native.State=="LOADED"&&native.Rows.Count==0&&native.Notice=="","HUD without a mix shows the loaded shell");
        var snapshot=new AmmoSnapshot("heat","HEAT 105","HEAT","apds_late","APDS late","APDS",true,12,new[]
            {new AmmoSnapshotRound("heat","HEAT 105","HEAT",9,false,true),new AmmoSnapshotRound("apds_late","APDS late","APDS",3,true,false)},false,null);
        var mix=AmmoHudFormat.Mix(snapshot,AmmoLoadState.Loaded,"x: not available in this era, skipped.");
        check(mix.Name=="APDS late"&&mix.Code=="APDS"&&mix.Rows.Count==2&&mix.Rows[0].Marker==">"&&mix.Rows[1].Count=="2 +1 gun"&&mix.Notice.Length>0,"HUD with a mix: round in the gun, rows, next-load marker, notice");
        check(AmmoHudFormat.Mix(snapshot,AmmoLoadState.Loaded).Key==AmmoHudFormat.Mix(snapshot,AmmoLoadState.Loaded).Key&&mix.Key!=native.Key,"HUD key changes only with shown values");
        var frozen=AmmoHudFormat.Mix(snapshot with{Frozen=true,FailureReason="The existing loaded round cannot be identified in this carried mix."},AmmoLoadState.Loaded);
        check(frozen.Name=="Mix unavailable"&&frozen.Notice.StartsWith("The existing loaded round",StringComparison.Ordinal),"frozen mix explains itself once on the HUD");
    }
}
