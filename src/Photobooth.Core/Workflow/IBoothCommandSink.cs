namespace Photobooth.Core.Workflow;

/// <summary>
/// Point d'injection de commandes dans le workflow, exposé aux composants externes (hôte admin)
/// sans leur donner accès à toute la surface du workflow. Implémenté par PhotoboothWorkflow.
/// </summary>
public interface IBoothCommandSink
{
    void Submit(BoothCommand command);
}
