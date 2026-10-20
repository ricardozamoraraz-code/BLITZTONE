using System.Numerics;
namespace MystieHook.Games.Wardog;
public enum WardogAimTarget { ClosestToCrosshair, ClosestToMe }
[Flags]
public enum WardogAimBoneMask { None=0, Head=1, Neck=2, Chest=4, Pelvis=8 }
public sealed record WardogAimSettings
{
 // Compatibility members only; retired smoothing is neither loaded nor saved or used for movement.
 [System.Text.Json.Serialization.JsonIgnore] public bool SmoothXEnabled {get;init;}
 [System.Text.Json.Serialization.JsonIgnore] public bool SmoothYEnabled {get;init;}
 public bool UseCalibration {get;init;} public WardogAimProfile Calibration {get;init;}
 public bool Enabled {get;init;}
 public bool ShowPrediction {get;init;}
 public bool UsePrediction {get;init;}
 public float BulletGravityCm {get;init;}=980;
 public float PredictionDotRadius {get;init;}=2;
 public bool ShowPredictionDetails {get;init;}
 public uint HeadPredictionColor {get;init;}=0xFF00CCFF;
 public WardogAimTarget AimTarget {get;init;}
 [System.Text.Json.Serialization.JsonIgnore] public bool NeedsPrediction => ShowPrediction || (Enabled && UsePrediction);
 [System.Text.Json.Serialization.JsonIgnore] public float SmoothX {get;init;}=10;
 [System.Text.Json.Serialization.JsonIgnore] public float SmoothY {get;init;}=20;
 public float Fov {get;init;}=80; public bool ShowFov {get;init;}=true;
 public int Key {get;init;}=2; public int Bone {get;init;}=1;
 public WardogAimBoneMask BoneMask {get;init;}
 public bool RandomizeBone {get;init;}
 public bool TargetLine {get;init;} public uint LineColor {get;init;}=0xFF5858ED;
 public bool TeamCheck {get;init;}=true;
 public float Distance {get;init;}=100;
 public bool Valid => float.IsFinite(BulletGravityCm)&&BulletGravityCm is >=0 and <=5000
 && float.IsFinite(PredictionDotRadius)&&PredictionDotRadius is >=1 and <=20
 && Enum.IsDefined(AimTarget) && (!UseCalibration || Calibration is { Valid:true }) && (Calibration is null || Calibration.Valid)
 && float.IsFinite(Fov)&&Fov is >=1 and <=600 && float.IsFinite(Distance)&&Distance is >=1 and <=10000 && Key is >=1 and <=254 && Bone is >=0 and <=4
 && (BoneMask & ~(WardogAimBoneMask.Head|WardogAimBoneMask.Neck|WardogAimBoneMask.Chest|WardogAimBoneMask.Pelvis))==0
 && (!RandomizeBone || BoneMask!=WardogAimBoneMask.None);
 public bool CalibrationReady(int width,int height,float fov) => UseCalibration && Calibration is {Valid:true} p
  && p.Width==width && p.Height==height && (p.LocalMouse || (float.IsFinite(fov)&&fov is >1 and <179));
 public static Vector2? Select(WardogReader.Frame frame,WardogAimSettings s,int width,int height) => Select(frame,s,width,height,out _);
 public static Vector2? Select(WardogReader.Frame frame,WardogAimSettings s,int width,int height,out ulong targetAddress)
 {
  targetAddress=0;
  if(!s.Enabled)return null;
  if(s.UsePrediction)return WardogPrediction.Select(frame,s,width,height,out targetAddress);
  var result=SelectPoint(frame,s,width,height,out var player,out _);
  targetAddress=player?.Address ?? 0;
  return result;
 }
 // Select the actual bone first; both movement and marker use this acquisition policy.
 public static Vector2? SelectPoint(WardogReader.Frame frame,WardogAimSettings s,int width,int height,out WardogReader.Player player,out WardogReader.Vec worldPoint)
  =>SelectPoint(frame,s,width,height,out player,out worldPoint,out _);
 public static Vector2? SelectPoint(WardogReader.Frame frame,WardogAimSettings s,int width,int height,out WardogReader.Player player,out WardogReader.Vec worldPoint,out int selectedBoneIndex)
 {
  player=null;worldPoint=default;selectedBoneIndex=-1;
  if(!s.Enabled||!Enum.IsDefined(s.AimTarget)||!frame.IsFresh||width<=0||height<=0)return null;
  float best=s.Fov*s.Fov;Vector2? result=null;var center=new Vector2(width/2f,height/2f);
  double bestRange=double.PositiveInfinity;
  foreach(var p in frame.Players)
  {
   double range=p.Position.Distance(frame.LocalPosition)/100;
   if(p.Dead||p.Health<=0||!double.IsFinite(range)||range>s.Distance || (s.TeamCheck&&p.TeamRelation!=WardogReader.Relation.Enemy))continue;
   var pose=PoseFor(p,s);
   int selected=s.Bone switch {0=>0,1=>1,2=>2,3=>5,_=>-1};
   for(int i=0;i<pose.Points.Length;i++)
   {
    bool selectedByMask=s.BoneMask!=WardogAimBoneMask.None
     ? (s.BoneMask&BoneMaskForIndex(i))!=0
     : selected<0||i==selected;
    if(!selectedByMask||!pose.Valid[i])continue;
    if(!WardogReader.Project(frame.Camera,pose.Points[i],width,height,out var point)||point.X<0||point.Y<0||point.X>width||point.Y>height)continue;
    float distance=Vector2.DistanceSquared(point,center);
    if(distance>=s.Fov*s.Fov)continue;
    if(s.AimTarget==WardogAimTarget.ClosestToMe
       ? range>bestRange || (range==bestRange && distance>=best)
       : distance>=best)continue;
    bestRange=range;best=distance;result=point;player=p;worldPoint=pose.Points[i];selectedBoneIndex=i;
   }
  }
  return result;
 }
 public static int[] EligibleBones(WardogReader.Frame frame,WardogReader.Player player,WardogAimSettings s,int width,int height)
 {
  if(!frame.IsFresh||width<=0||height<=0)return [];
  var pose=PoseFor(player,s);var result=new List<int>();var center=new Vector2(width/2f,height/2f);
  for(int i=0;i<pose.Points.Length;i++)
  {
   bool selected=s.BoneMask!=WardogAimBoneMask.None
    ?(s.BoneMask&BoneMaskForIndex(i))!=0
    :s.Bone==4||i==(s.Bone switch {0=>0,1=>1,2=>2,3=>5,_=>-1});
   if(!selected||i>=pose.Valid.Length||!pose.Valid[i]
    ||!WardogReader.Project(frame.Camera,pose.Points[i],width,height,out var point)
    ||point.X<0||point.Y<0||point.X>width||point.Y>height
    ||Vector2.DistanceSquared(point,center)>=s.Fov*s.Fov)continue;
   result.Add(i);
  }
  return result.ToArray();
 }
 private static WardogReader.SkeletonPose PoseFor(WardogReader.Player player,WardogAimSettings s)
  =>s.BoneMask==WardogAimBoneMask.None&&s.Bone==4&&player.Pose.HasHead?player.Pose:player.HeadPose;
 public static WardogAimBoneMask BoneMaskForIndex(int index)=>index switch
 {0=>WardogAimBoneMask.Head,1=>WardogAimBoneMask.Neck,2=>WardogAimBoneMask.Chest,5=>WardogAimBoneMask.Pelvis,_=>WardogAimBoneMask.None};
}
