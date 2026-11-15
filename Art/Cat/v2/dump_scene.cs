var sb = new System.Text.StringBuilder();
var all = UnityEngine.Object.FindObjectsOfType<UnityEngine.Transform>(true);
string[] keys = new string[]{"cat","Cat","bed","Bed","bowl","Bowl","food","Food","Sleep","sleep","Eat","eat","Opening","opening"};
System.Collections.Generic.List<UnityEngine.Transform> picked = new System.Collections.Generic.List<UnityEngine.Transform>();
for (int i=0;i<all.Length;i++){
  string n = all[i].name;
  bool hit=false;
  for(int k=0;k<keys.Length;k++){ if(n.Contains(keys[k])){hit=true;break;} }
  if(hit) picked.Add(all[i]);
}
sb.AppendLine("TOTAL_TRANSFORMS="+all.Length+"  PICKED="+picked.Count);
for(int i=0;i<picked.Count;i++){
  var t=picked[i];
  string path=t.name; var p=t.parent; int guard=0;
  while(p!=null && guard<12){ path=p.name+"/"+path; p=p.parent; guard++; }
  sb.AppendLine(path+"  | pos="+t.position.ToString("F3")+" rot="+t.eulerAngles.ToString("F1")+" scale="+t.localScale.ToString("F3")+" active="+t.gameObject.activeInHierarchy);
}
return sb.ToString();
