namespace LinuxHub.Features.InstallWizard.Services
{
    public interface IDiskPartitioningService
    {
        /// <summary>
        /// Encolhe a partição indicada (disco/partição reais, selecionados no wizard) em
        /// <paramref name="bytesToFree"/>, via <c>Resize-Partition</c> em processo elevado.
        /// Não cria partição nem sistema de arquivos.
        ///
        /// Recebe bytes, e não GB, porque o encolhimento do dual-boot agora precisa somar o
        /// que o usuário pediu no slider (múltiplo de GB) com o que a partição de staging e a
        /// semente consomem (MB) — e o design pede UM shrink só, não uma sequência deles
        /// (ver design.md D4 do change iso-staging-partition).
        ///
        /// Lança se o processo elevado não puder ser iniciado ou se o encolhimento não couber
        /// (espaço livre insuficiente, arquivos imóveis do Windows).
        /// </summary>
        void ShrinkPartition(int diskIndex, int partitionIndex, long bytesToFree);

        /// <summary>
        /// Garante que o disco tenha ao menos <paramref name="requiredBytes"/> contíguos não
        /// alocados, encolhendo a maior partição NTFS se necessário. Usado no modo substituir,
        /// onde o usuário escolhe um DISCO e não uma partição, então não há alvo de
        /// encolhimento informado — e sem isso o disco cheio de Windows não tem onde acomodar
        /// a staging nem a semente.
        ///
        /// No-op quando já existe espaço suficiente: no dual-boot o encolhimento do slider já
        /// abriu o vão, e encolher de novo tiraria espaço do usuário sem motivo.
        /// </summary>
        void EnsureUnallocatedSpace(int diskIndex, long requiredBytes);
    }
}
