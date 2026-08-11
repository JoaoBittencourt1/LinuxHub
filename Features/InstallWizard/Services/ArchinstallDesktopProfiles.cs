using System.Collections.Generic;
using System.Linq;

namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Um ambiente gráfico oferecido pelo <c>archinstall</c>: o nome exato do profile e o
    /// greeter que o acompanha.
    ///
    /// O greeter não é detalhe: é o que faz a sessão gráfica subir sozinha no primeiro boot.
    /// Instalar o ambiente sem ele entrega uma máquina que liga num terminal — para o público
    /// deste app, indistinguível de uma falha.
    /// </summary>
    public sealed record ArchinstallDesktopProfile(string ProfileName, string Greeter);

    /// <summary>
    /// Os ambientes gráficos que o <c>archinstall</c> 4.4 conhece, com o greeter padrão de
    /// cada um — ambos lidos do pacote real (<c>default_profiles/desktops/</c>), não da
    /// documentação. Nome de profile é casado por igualdade exata do outro lado
    /// (<c>get_profile_by_name</c>), então um nome divergente aqui não dá erro: dá uma
    /// instalação sem ambiente gráfico nenhum.
    ///
    /// Nomes próprios são dado, isentos de localização por §4.
    /// </summary>
    public static class ArchinstallDesktopProfiles
    {
        /// <summary>O profile "guarda-chuva" que agrupa os ambientes de desktop; o escolhido
        /// entra em <c>details</c>.</summary>
        public const string DesktopProfileName = "Desktop";

        /// <summary>O greeter padrão do projeto. Padronizar num só reduz a superfície do que
        /// precisa funcionar no primeiro boot: o SDDM sobe tanto sessão Wayland quanto X11 e
        /// serve a todos os ambientes abaixo.</summary>
        private const string DefaultGreeter = "sddm";

        public static IReadOnlyList<ArchinstallDesktopProfile> All { get; } =
        [
            new("GNOME", DefaultGreeter),
            new("KDE Plasma", DefaultGreeter),
            new("Xfce4", DefaultGreeter),
            new("Cinnamon", DefaultGreeter),
            new("Mate", DefaultGreeter),
            new("Budgie", DefaultGreeter),
            new("Hyprland", DefaultGreeter),
            new("Sway", DefaultGreeter),
            new("i3-wm", DefaultGreeter),

            // A exceção, e não por descuido: a sessão do COSMIC depende do greeter próprio dela
            // (é ele que prepara o ambiente que o compositor espera). Trocá-lo pelo SDDM aqui
            // entregaria justamente o que o greeter existe para evitar — uma máquina que liga
            // sem chegar ao desktop.
            new("Cosmic", "cosmic-greeter"),
        ];

        public static ArchinstallDesktopProfile? Find(string? profileName) =>
            string.IsNullOrWhiteSpace(profileName)
                ? null
                : All.FirstOrDefault(profile => profile.ProfileName == profileName);
    }
}
