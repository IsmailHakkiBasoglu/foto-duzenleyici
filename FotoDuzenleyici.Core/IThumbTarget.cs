using System.Windows.Media;

namespace FotoDuzenleyici.Core;

/// <summary>Küçük resmi <see cref="ThumbnailLoader"/> ile yüklenen bir satır ya da karo.</summary>
public interface IThumbTarget
{
    MediaFile File { get; }
    ImageSource? Thumb { get; set; }
    bool ThumbFailed { get; set; }
}
