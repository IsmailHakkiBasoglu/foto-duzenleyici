namespace FotoDuzenleyici.ViewModels;

/// <summary>Sayfadan çıkmadan önce sorulacak bir soru varsa (ör. ay görünümünde işaretli dosyalar); yoksa null.</summary>
public interface ILeaveGuard
{
    string? LeaveQuestion { get; }
}
