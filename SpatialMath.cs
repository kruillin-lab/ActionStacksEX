using System;
using System.Numerics;

namespace ActionStacksEX;

public enum ActionShape
{
    Circle = 2,
    Cone = 3,
    Line = 4,
}

public static unsafe class SpatialMath
{
    public static Vector2 Flatten(this Vector3 v) => new(v.X, v.Z);
    public static float FlatDist(this Vector3 a, Vector3 b) => Vector2.Distance(a.Flatten(), b.Flatten());
    public static float FlatDistSquared(this Vector3 a, Vector3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }
    public static Vector2 NormalizeSafe(this Vector2 v)
    {
        var len = v.Length();
        return len > 0 ? v / len : Vector2.Zero;
    }

    public static int CircleHitCount(Vector3 center, float radius, ReadOnlySpan<Vector3> enemyPositions)
    {
        int count = 0;
        var r2 = radius * radius;
        foreach (ref readonly var pos in enemyPositions)
        {
            var dx = pos.X - center.X;
            var dz = pos.Z - center.Z;
            if (dx * dx + dz * dz <= r2) count++;
        }
        return count;
    }

    public static float AngleToPoint(Vector3 point, Vector3 origin, Vector2 facing)
    {
        var toPoint = (point.Flatten() - origin.Flatten()).NormalizeSafe();
        return MathF.Acos(Vector2.Dot(facing, toPoint));
    }

    public static bool IsInCone(Vector3 point, Vector3 origin, Vector2 facing, float arcRad, float range)
    {
        var dist = origin.FlatDist(point);
        if (dist > range) return false;
        return AngleToPoint(point, origin, facing) <= arcRad / 2f;
    }

    public static bool IsInLine(Vector3 point, Vector3 origin, Vector2 facing, float range, float halfWidth)
    {
        var toPoint = point.Flatten() - origin.Flatten();
        var dist = toPoint.Length();
        if (dist > range) return false;
        if (dist < 0.0001f) return true;
        var dir = toPoint / dist;
        var dot = Vector2.Dot(dir, facing);
        if (dot <= 0) return false;
        var lateral = MathF.Sqrt(MathF.Max(0, 1f - dot * dot));
        return lateral <= halfWidth / dist;
    }

    public static Vector3 GetPartyCentroid()
    {
        var members = Hypostasis.Game.Common.GetPartyMembers();
        float sumX = 0, sumZ = 0;
        int count = 0;
        foreach (var m in members)
        {
            var obj = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)m;
            if (obj == null) continue;
            var hp = ((FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)obj)->CharacterData.Health;
            if (hp == 0) continue;
            sumX += obj->Position.X;
            sumZ += obj->Position.Z;
            count++;
        }
        if (count == 0) return Vector3.Zero;
        var localPlayer = Hypostasis.Dalamud.DalamudApi.ClientState.LocalPlayer;
        return new Vector3(sumX / count, localPlayer?.Position.Y ?? 0, sumZ / count);
    }

    public static FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject* FindBestAoETarget(uint actionId)
    {
        if (!ActionStacksEX.actionSheet.TryGetValue(actionId, out var a)) return null;
        var shape = (ActionShape)a.CastType;
        var radius = a.EffectRange;

        var localPlayer = Hypostasis.Dalamud.DalamudApi.ClientState.LocalPlayer;
        if (localPlayer == null) return null;

        var playerPos = localPlayer.Position;

        var rot = localPlayer->Rotation;
        var facing = new Vector2(MathF.Cos(rot), MathF.Sin(rot));

        var positions = _enemyPosBuffer;
        int n = 0;
        foreach (var obj in Hypostasis.Dalamud.DalamudApi.Svc.Objects)
        {
            if (n >= 50) break;
            if (obj is ECommons.GameFunctions.IBattleChara bc && Extensions.IsEnemy(bc) && !bc.IsDead)
            {
                positions[n++] = bc.Position;
            }
        }
        var span = positions.AsSpan(0, n);

        FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject* best = null;
        int bestCount = 0;

        foreach (var obj in Hypostasis.Dalamud.DalamudApi.Svc.Objects)
        {
            if (obj is not ECommons.GameFunctions.IBattleChara bc || !Extensions.IsEnemy(bc) || bc.IsDead) continue;
            var enemyPos = bc.Position;

            int count = shape switch
            {
                ActionShape.Circle => CircleHitCount(enemyPos, radius, span),
                ActionShape.Cone => IsInCone(enemyPos, playerPos, facing, radius, radius) ? 1 : 0,
                ActionShape.Line => IsInLine(enemyPos, playerPos, facing, radius, radius) ? 1 : 0,
                _ => 1,
            };

            if (count > bestCount)
            {
                bestCount = count;
                best = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)bc.Address;
            }
            else if (count == bestCount && count > 0)
            {
                var dxBest = enemyPos.X - playerPos.X;
                var dzBest = enemyPos.Z - playerPos.Z;
                var distBestSq = dxBest * dxBest + dzBest * dzBest;
                var dxCurrent = best->Position.X - playerPos.X;
                var dzCurrent = best->Position.Z - playerPos.Z;
                var distCurrentSq = dxCurrent * dxCurrent + dzCurrent * dzCurrent;
                if (distBestSq < distCurrentSq)
                    best = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)bc.Address;
            }
        }
        return best;
    }
}
