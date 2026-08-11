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

        public static IReadOnlyList<ArchinstallDesktopProfile> All { get; } =
        [
            new("GNOME", "gdm"),
            new("KDE Plasma", "plasma-login-manager"),
            new("Xfce4", "lightdm-gtk-greeter"),
            new("Cinnamon", "lightdm-gtk-greeter"),
            new("Mate", "lightdm-gtk-greeter"),
            new("Budgie", "lightdm-slick-greeter"),
            new("Cosmic", "cosmic-greeter"),
            new("Hyprland", "sddm"),
            new("Sway", "lightdm-gtk-greeter"),
            new("i3-wm", "lightdm-gtk-greeter"),
        ];

        public static ArchinstallDesktopProfile? Find(string? profileName) =>
            string.IsNullOrWhiteSpace(profileName)
                ? null
                : All.FirstOrDefault(profile => profile.ProfileName == profileName);
    }
}
