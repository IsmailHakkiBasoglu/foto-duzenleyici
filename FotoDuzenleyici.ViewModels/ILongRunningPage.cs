namespace FotoDuzenleyici.ViewModels;

/// <summary>Kabuğun, üzerinde uzun iş süren sayfayı tanıdığı arayüz.</summary>
public interface ILongRunningPage
{
    /// <summary>Gezinme kapalı mı (tarama dahil).</summary>
    bool IsLocked { get; }
    /// <summary>Pencere kapatılırken sorulsun mu: dosyalar işleniyorsa evet, yalnız taramaysa hayır.</summary>
    bool MustConfirmClose { get; }
    Task? RunningTask { get; }
    string CloseQuestion { get; }
    void RequestStop();
}
