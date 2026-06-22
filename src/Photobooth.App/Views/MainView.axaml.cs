using System;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Photobooth.App.ViewModels;

namespace Photobooth.App.Views;

public partial class MainView : UserControl
{
    // Must match the static RotateTransform angles in MainView.axaml (Card0, Card1, Card2).
    private static readonly double[] CardAngles = [-10.848, 7.0, -20.461];

    private const double AnimDurationMs  = 400.0;
    private const double AnimStartY      = 800.0;
    private const double RevealDelayT    = 0.7;  // fraction of card anim at which the wipe begins
    private const double RevealDurationMs = 600.0;
    private const double RevealDistance  = 500.0; // enough to exit the clipped photo area

    private readonly DispatcherTimer?[] _animTimers   = new DispatcherTimer?[3];
    private readonly DispatcherTimer?[] _revealTimers = new DispatcherTimer?[3];
    private readonly bool[]             _revealStarted = new bool[3];
    private DispatcherTimer? _flashTimer;
    private DispatcherTimer? _pulseTimer;

    public MainView()
    {
        InitializeComponent();
        Focusable = true; // so keyboard triggers work in single-view (Pi) mode
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not MainViewModel vm) return;

        Border[]    borders = [Card0, Card1, Card2];
        Rectangle[] reveals = [RevealOverlay0, RevealOverlay1, RevealOverlay2];

        for (var i = 0; i < vm.Cards.Length; i++)
        {
            var idx = i;
            vm.Cards[i].PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(CardViewModel.ZIndex) || vm.Cards[idx].ZIndex != 100)
                    return;

                var hasImage = vm.Cards[idx].IsImageVisible;
                if (hasImage)
                {
                    // #UI-2 : réinitialiser le rideau blanc (couvre la photo dès le départ)
                    var revTrans = (TranslateTransform)reveals[idx].RenderTransform!;
                    revTrans.Y = 0.0;
                    reveals[idx].Opacity = 1.0;
                    _revealStarted[idx] = false;

                    // #UI-1 : flash au déclenchement
                    FlashWhite();
                }
                else
                {
                    // Pas de révélation pour les cartes texte
                    reveals[idx].Opacity = 0.0;
                    _revealStarted[idx] = true;
                }

                AnimateCardIn(idx, borders[idx], reveals[idx], hasImage);
            };
        }

        // #UI-4 : pulse à chaque changement de chiffre du countdown photo
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.PhotoCountdownText))
                PulseCountdown();
        };
    }

    // ---- #UI-1 : flash blanc ------------------------------------------------

    private void FlashWhite()
    {
        _flashTimer?.Stop();
        _flashTimer = null;
        FlashOverlay.Opacity = 1.0;

        var startTime = DateTime.UtcNow;
        const double durationMs = 300.0;

        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var t = Math.Min((DateTime.UtcNow - startTime).TotalMilliseconds / durationMs, 1.0);
            FlashOverlay.Opacity = 1.0 - t;
            if (t >= 1.0)
            {
                timer.Stop();
                _flashTimer = null;
                FlashOverlay.Opacity = 0.0;
            }
        };
        _flashTimer = timer;
        timer.Start();
    }

    // ---- #UI-2 : révélation Polaroid ----------------------------------------

    private void StartReveal(int cardIndex, Rectangle overlay)
    {
        _revealTimers[cardIndex]?.Stop();
        _revealTimers[cardIndex] = null;

        var revTrans = (TranslateTransform)overlay.RenderTransform!;
        var startTime = DateTime.UtcNow;

        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var t = Math.Min((DateTime.UtcNow - startTime).TotalMilliseconds / RevealDurationMs, 1.0);
            revTrans.Y = RevealDistance * EaseOutCubic(t);
            if (t >= 1.0)
            {
                timer.Stop();
                _revealTimers[cardIndex] = null;
                overlay.Opacity = 0.0;
            }
        };
        _revealTimers[cardIndex] = timer;
        timer.Start();
    }

    // ---- #UI-4 : pulse chiffre countdown ------------------------------------

    private void PulseCountdown()
    {
        _pulseTimer?.Stop();
        _pulseTimer = null;

        var scale = (ScaleTransform)PhotoCountdownLabel.RenderTransform!;
        scale.ScaleX = 1.3;
        scale.ScaleY = 1.3;

        var startTime = DateTime.UtcNow;
        const double pulseDurationMs = 400.0;

        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var t = Math.Min((DateTime.UtcNow - startTime).TotalMilliseconds / pulseDurationMs, 1.0);
            var s = 1.3 - 0.3 * EaseOutCubic(t); // 1.3 → 1.0
            scale.ScaleX = s;
            scale.ScaleY = s;
            if (t >= 1.0)
            {
                timer.Stop();
                _pulseTimer = null;
            }
        };
        _pulseTimer = timer;
        timer.Start();
    }

    // ---- Slide-in de carte --------------------------------------------------

    private void AnimateCardIn(int cardIndex, Border card, Rectangle revealOverlay, bool hasImage)
    {
        var group     = (TransformGroup)card.RenderTransform!;
        var translate = (TranslateTransform)group.Children[0];
        var rotate    = (RotateTransform)group.Children[1];

        _animTimers[cardIndex]?.Stop();
        _animTimers[cardIndex] = null;

        translate.Y  = AnimStartY;
        rotate.Angle = CardAngles[cardIndex]; // static tilt, never animated
        card.Opacity = 0.0;

        var startTime = DateTime.UtcNow;

        var timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        timer.Tick += (_, _) =>
        {
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            var t       = Math.Min(elapsed / AnimDurationMs, 1.0);

            // EaseOutBack: slides up and slightly overshoots before settling — the "landing bump".
            translate.Y  = AnimStartY * (1.0 - EaseOutBack(t));
            card.Opacity = Math.Min(t / 0.35, 1.0);

            // #UI-2 : démarrer le wipe quand la carte a atteint sa position (t≈0.7 = juste atterrie)
            if (hasImage && !_revealStarted[cardIndex] && t >= RevealDelayT)
            {
                _revealStarted[cardIndex] = true;
                StartReveal(cardIndex, revealOverlay);
            }

            if (t >= 1.0)
            {
                timer.Stop();
                _animTimers[cardIndex] = null;
                translate.Y  = 0.0;
                card.Opacity = 1.0;
            }
        };

        _animTimers[cardIndex] = timer;
        timer.Start();
    }

    // ---- Easing functions ---------------------------------------------------

    // Slides past the target then snaps back — gives the card a physical "landing" feel.
    private static double EaseOutBack(double t)
    {
        const double c1 = 0.75;
        const double c3 = c1 + 1.0;
        return 1.0 + c3 * Math.Pow(t - 1.0, 3) + c1 * Math.Pow(t - 1.0, 2);
    }

    private static double EaseOutCubic(double t) => 1.0 - Math.Pow(1.0 - t, 3.0);
}
