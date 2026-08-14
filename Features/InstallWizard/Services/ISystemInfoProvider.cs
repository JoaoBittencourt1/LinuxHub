using LinuxHub.Features.InstallWizard.Models;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// As três informações regionais lidas da configuração do Windows. Cada uma vem marcada
    /// com "foi reconhecida" ou "é o padrão declarado" (ver <see cref="DetectedRegionalSetting"/>):
    /// o wizard apresenta as duas coisas, e é isso que impede uma detecção sem correspondência
    /// de virar um valor arbitrário gravado sem ninguém ver.
    /// </summary>
    public interface ISystemInfoProvider
    {
        DetectedRegionalSetting GetLocale();
        DetectedRegionalSetting GetKeymap();
        DetectedRegionalSetting GetTimezone();
    }
}
