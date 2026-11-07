namespace FotoDuzenleyici.ViewModels;

/// <summary>Plandan ekranlarda gösterilen sayılar. <see cref="ApplyCount"/>: "Zaten yerinde" dışındaki öğeler.</summary>
public sealed record PlanSummary(int MoveCount, long MoveBytes, int InPlace, int DupCount, long DupBytes,
    int Undated, int Renamed, int ApplyCount, int SimilarCount = 0, int FlawedCount = 0)
{
    public static PlanSummary From(Plan plan, PlanOptions opt)
    {
        var moves = plan.Items.Where(i => i.Action == PlanAction.Move).ToList();
        var dups = plan.Items.Where(i => i.Action == PlanAction.Duplicate).ToList();
        return new PlanSummary(
            moves.Count, moves.Sum(i => i.File.Size),
            plan.Items.Count(i => i.Action == PlanAction.AlreadyInPlace),
            dups.Count, dups.Sum(i => i.File.Size),
            moves.Count(i => IsUndated(i, opt)),
            plan.Items.Count(i => i.Renamed),
            plan.Items.Count(i => i.Action != PlanAction.AlreadyInPlace),
            plan.Items.Count(i => i.Action == PlanAction.Similar),
            plan.Items.Count(i => i.Action == PlanAction.Flawed));
    }

    /// <summary>Hedefin arşive göre ilk klasörü "Tarihsiz" ise.</summary>
    static bool IsUndated(PlanItem i, PlanOptions opt)
    {
        var rel = Path.GetRelativePath(opt.DestinationRoot, i.TargetDir);
        var first = rel.Split('\\', '/')[0];
        return first.Equals(opt.UndatedFolderName, StringComparison.OrdinalIgnoreCase);
    }
}
