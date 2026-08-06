using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LinuxHub.Features.InstallWizard.Views.Converters
{
    /// <summary>
    /// <c>false</c> vira <see cref="Visibility.Visible"/>. Existe para os avisos ligados a
    /// uma propriedade afirmativa do modelo (<c>IsTested</c>), onde o elemento aparece
    /// justamente quando ela é falsa — negar no modelo só para servir à View inverteria a
    /// leitura natural do dado.
    /// </summary>
    public sealed class FalseToVisibleConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
