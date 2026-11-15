var sb = new System.Text.StringBuilder();
System.Action<string> dump = delegate(string path) {
  var go = UnityEngine.GameObject.Find(path);
  if (go == null) { 
    // try deep search
    var all = UnityEngine.Object.FindObjectsOfType<UnityEngine.Transform>(true);
    for (int i=0;i<all.Length;i++) if (all[i].name==path) { go=all[i].gameObject; break; }
  }
  if (go == null) { sb.AppendLine("MISSING: "+path); return; }
  sb.AppendLine("== "+path+"  pos="+go.transform.position.ToString("F3")+" rot="+go.transform.eulerAngles.ToString("F1"));
  var comps = go.GetComponents<UnityEngine.Component>();
  for (int i=0;i<comps.Length;i++){
    var c=comps[i];
    if (c==null) continue;
    string extra="";
    var r = c as UnityEngine.Renderer;
    if (r!=null) extra = " bounds(center="+r.bounds.center.ToString("F3")+" size="+r.bounds.size.ToString("F3")+") enabled="+r.enabled;
    var col = c as UnityEngine.Collider;
    if (col!=null) extra = " colBounds(center="+col.bounds.center.ToString("F3")+" size="+col.bounds.size.ToString("F3")+") isTrigger="+col.isTrigger;
    sb.AppendLine("   - "+c.GetType().Name+extra);
  }
};
string[] paths = new string[]{"CAT_Nocturne","CAT_Nocturne/CatRig","Cat_Bed_OpeningPosition","Cat_FloorExitPosition",
  "NOCTURNE/Furniture/M_PR_Bed","NOCTURNE/Furniture/COL_PR_Bed","NOCTURNE/Furniture/M_PR_BedHead",
  "NOCTURNE/Furniture/PROP_CatBed","NOCTURNE/Furniture/PROP_CatBed/BedCushion","NOCTURNE/Furniture/PROP_CatBed/BedRim",
  "NOCTURNE/Furniture/PROP_CatBed/CatSleepPoint","CatFoodBowl","CatFoodBowl/BowlBody","CatFoodBowl/CatFood","CatFoodBowl/FoodPoint","CatFoodBowl/FoodApproachPoint"};
for(int i=0;i<paths.Length;i++) dump(paths[i]);
return sb.ToString();
