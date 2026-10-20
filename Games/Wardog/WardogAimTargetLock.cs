using System.Numerics;

namespace MystieHook.Games.Wardog;

// One identity per hold. Temporary missing samples pause output without releasing identity.
// Single output-worker owner; immutable identity can also constrain render snapshots.
public sealed class WardogAimTargetLock
{
    public sealed record Identity(ulong World, ulong Controller, ulong LocalPawn, ulong Address, ulong State, ulong Mesh, ulong Asset,
        int SelectedBone = -1)
    {
        public WardogReader.Frame Filter(WardogReader.Frame frame)
        {
            if (!frame.IsFresh || frame.WorldAddress != World || frame.LocalController != Controller || frame.LocalPawn != LocalPawn)
                return WardogReader.Frame.Empty("Aim target context changed");
            var player = frame.Players.FirstOrDefault(p => p.Address == Address && p.SourceState == State && p.SourceMesh == Mesh && p.SourceAsset == Asset);
            return player == null ? WardogReader.Frame.Empty("Aim target lost") : frame with { Players = [player] };
        }
    }
    public Identity Target { get; private set; }
    private bool _blocked;
    public bool AwaitingRelease=>_blocked;
    private int _key;
    private int _randomBone = -1;
    public void Block() { _blocked = true; Target = null; }

    public Vector2? Select(WardogReader.Frame frame, WardogAimSettings settings, int width, int height,
        bool held, bool outputAllowed, out ulong address)
    {
        address = 0;
        if (!held) { _blocked = false; Target = null; _key = settings.Key; _randomBone=-1; return null; }
        if (!settings.Enabled || (_key != 0 && _key != settings.Key)) Block();
        _key = settings.Key;
        if (_blocked) return null;
        if (!outputAllowed || !frame.IsFresh) return null;
        if (Target == null)
        {
            var acquisition = settings.UsePrediction ? settings with {TeamCheck=true} : settings;
            if (WardogAimSettings.SelectPoint(frame, acquisition, width, height, out var player, out _,out int selectedBone) == null)
            { return null; }
            if(player.SourceState==0||player.SourceMesh==0||player.SourceAsset==0)return null;
            if(settings.BoneMask!=WardogAimBoneMask.None&&settings.RandomizeBone)
            {
                if(_randomBone<0)
                {
                    var choices=WardogAimSettings.EligibleBones(frame,player,acquisition,width,height);
                    if(choices.Length==0)return null;
                    _randomBone=choices[Random.Shared.Next(choices.Length)];
                }
                selectedBone=_randomBone;
            }
            int lockedBone=settings.BoneMask==WardogAimBoneMask.None?-1:selectedBone;
            Target = new(frame.WorldAddress, frame.LocalController, frame.LocalPawn, player.Address, player.SourceState, player.SourceMesh, player.SourceAsset,lockedBone);
        }
        var locked = frame.Players.FirstOrDefault(p => p.Address == Target.Address);
        if (frame.WorldAddress != Target.World || frame.LocalController != Target.Controller || frame.LocalPawn != Target.LocalPawn
            || (locked != null && ((locked.SourceState!=0&&locked.SourceState != Target.State) || (locked.SourceMesh!=0&&locked.SourceMesh != Target.Mesh) || (locked.SourceAsset!=0&&locked.SourceAsset != Target.Asset)
                || locked.Dead || locked.Health == 0 || ((settings.TeamCheck || settings.UsePrediction) && locked.TeamRelation == WardogReader.Relation.Friendly))))
        { Block(); return null; }
        var lockedSettings=Target.SelectedBone<0?settings:settings with
        {Bone=Target.SelectedBone,BoneMask=WardogAimSettings.BoneMaskForIndex(Target.SelectedBone),RandomizeBone=false};
        var point = WardogAimSettings.Select(Target.Filter(frame), lockedSettings, width, height, out address);
        if (point == null) address = 0;
        return point;
    }
}
