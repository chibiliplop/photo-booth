namespace Photobooth.App;

/// <summary>
/// Porte d_entree de l_ecran d_accueil admin affiche au boot. Tant qu_elle est armee, le 1er appui
/// "photo" est consomme pour fermer l_ecran (aucune capture) ; les appuis suivants passent normalement.
/// Logique extraite du code-behind UI pour etre testable.
/// </summary>
public sealed class StartupNoticeGate
{
    private bool _pending;

    /// <summary>Vrai tant que l_ecran d_accueil est affiche et n_a pas encore ete ferme.</summary>
    public bool Pending => _pending;

    /// <summary>Arme la porte (ecran affiche).</summary>
    public void Arm() => _pending = true;

    /// <summary>
    /// Consomme un appui photo. Retourne true si l_appui a ferme l_ecran (donc PAS de capture),
    /// false si l_ecran etait deja ferme (capture normale).
    /// </summary>
    public bool ConsumePress()
    {
        var was = _pending;
        _pending = false;
        return was;
    }
}
