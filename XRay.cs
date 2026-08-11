using System;
using System.Collections.Generic;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace ActionStacksEX;

/// <summary>
/// Stack X-Ray: records a decision trace for every action stack evaluation so users can
/// see exactly why a stack fired, redirected, or fell through. Zero overhead unless
/// <see cref="Capturing"/> is enabled. Session-only state; nothing is persisted.
/// All access happens on the game's main thread (UseAction hook + ImGui draw).
/// </summary>
public static unsafe class XRay
{
    public enum StepKind
    {
        Pass,
        Fail,
        Info,
        Redirect
    }

    public readonly record struct Step(StepKind Kind, string Label, string Detail);

    public sealed class Evaluation
    {
        public DateTime Time;
        public uint TriggerID;
        public string TriggerName = string.Empty;
        public uint ModifierKeys;
        public string Outcome = string.Empty;
        public StepKind OutcomeKind = StepKind.Info;
        public bool DryRun;
        public readonly List<Step> Steps = [];
    }

    public const int Capacity = 64;

    public static bool Capturing = false;
    public static bool DryRun = false;
    public static bool DryRunActive => Capturing && DryRun;

    /// <summary>Newest evaluation first.</summary>
    public static readonly LinkedList<Evaluation> Buffer = new();

    private static Evaluation current;

    public static void Begin(uint triggerID, uint modifierKeys)
    {
        if (!Capturing) return;
        current = new Evaluation
        {
            Time = DateTime.Now,
            TriggerID = triggerID,
            TriggerName = ActionName(triggerID),
            ModifierKeys = modifierKeys,
            DryRun = DryRunActive
        };
    }

    public static void AddStep(StepKind kind, string label, string detail = "") => current?.Steps.Add(new Step(kind, label, detail));

    public static void Commit(StepKind kind, string outcome)
    {
        if (current == null) return;
        current.Outcome = outcome;
        current.OutcomeKind = kind;
        Buffer.AddFirst(current);
        while (Buffer.Count > Capacity)
            Buffer.RemoveLast();
        current = null;
    }

    /// <summary>Commit only if at least one step was recorded; otherwise drop silently (keeps plain rotation presses out of the buffer).</summary>
    public static void CommitIfRelevant(StepKind kind, string outcome)
    {
        if (current == null) return;
        if (current.Steps.Count == 0)
        {
            current = null;
            return;
        }
        Commit(kind, outcome);
    }

    public static void Clear()
    {
        Buffer.Clear();
        current = null;
    }

    public static string ActionName(uint id)
        => ActionStacksEX.actionSheet != null && ActionStacksEX.actionSheet.TryGetValue(id, out var a) ? $"{a.Name} ({id})" : $"#{id}";

    public static string StatusName(uint id)
        => ActionStacksEX.statusSheet != null && ActionStacksEX.statusSheet.TryGetValue(id, out var s) ? $"{s.Name} ({id})" : $"#{id}";

    public static string ObjectName(GameObject* obj)
    {
        if (obj == null) return "<null>";
        try
        {
            return obj->NameString;
        }
        catch
        {
            return $"<0x{(nint)obj:X}>";
        }
    }

    public static string Mods(uint keys)
    {
        var parts = new List<string>(3);
        if ((keys & 1) != 0) parts.Add("Shift");
        if ((keys & 2) != 0) parts.Add("Ctrl");
        if ((keys & 4) != 0) parts.Add("Alt");
        return parts.Count > 0 ? string.Join("+", parts) : "none";
    }

    public static string Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ActionStacks(OMP) X-Ray trace — {DateTime.Now:yyyy-MM-dd HH:mm:ss} — {Buffer.Count} evaluation(s), newest first");
        foreach (var e in Buffer)
        {
            sb.AppendLine($"[{e.Time:HH:mm:ss.fff}] {e.TriggerName} (mods held: {Mods(e.ModifierKeys)}){(e.DryRun ? " [DRY-RUN]" : string.Empty)} — {e.Outcome}");
            foreach (var s in e.Steps)
                sb.AppendLine($"    {s.Kind,-8} {s.Label}{(string.IsNullOrEmpty(s.Detail) ? string.Empty : $": {s.Detail}")}");
        }
        return sb.ToString();
    }
}
