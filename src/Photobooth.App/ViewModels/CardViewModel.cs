using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Photobooth.App.ViewModels;

/// <summary>
/// One of the three scattered photo cards. Content (image or text) cycles across the cards while each
/// card keeps a fixed rotation in the view; the newest card is brought to the front via <see cref="ZIndex"/>.
/// </summary>
public sealed class CardViewModel : ViewModelBase
{
    private Bitmap? _image;
    private string _message = string.Empty;
    private bool _isImageVisible;
    private bool _isTextVisible;
    private int _zIndex = 1;

    public Bitmap? Image
    {
        get => _image;
        set => SetField(ref _image, value);
    }

    public string Message
    {
        get => _message;
        set => SetField(ref _message, value);
    }

    public bool IsImageVisible
    {
        get => _isImageVisible;
        set => SetField(ref _isImageVisible, value);
    }

    public bool IsTextVisible
    {
        get => _isTextVisible;
        set => SetField(ref _isTextVisible, value);
    }

    public int ZIndex
    {
        get => _zIndex;
        set
        {
            if (SetField(ref _zIndex, value))
                Raise(nameof(CardShadow));
        }
    }

    // #UI-5: la carte active (ZIndex=100) reçoit une ombre plus profonde pour "sortir" de la pile.
    public BoxShadows CardShadow => _zIndex == 100
        ? BoxShadows.Parse("-15 15 35 0 #88000000")
        : BoxShadows.Parse("-10 10 20 0 #66000000");
}
