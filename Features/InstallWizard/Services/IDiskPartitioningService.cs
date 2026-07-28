namespace LinuxHub.Features.InstallWizard.Services
{
    public interface IDiskPartitioningService
    {
        /// <summary>
        /// Encolhe a partição indicada (disco/partição reais, selecionados no wizard) em
        /// <paramref name="sizeInGb"/>, via <c>Resize-Partition</c> em processo elevado. Não
        /// cria partição nem sistema de arquivos — isso é responsabilidade exclusiva do
        /// lib/disk.sh do lado Linux, executado depois do reboot (ver design.md D1).
        /// Lança se o processo elevado não puder ser iniciado ou se o encolhimento não
        /// couber (espaço livre insuficiente, arquivos imóveis do Windows).
        /// </summary>
        void ShrinkPartition(int diskIndex, int partitionIndex, int sizeInGb);
    }
}
