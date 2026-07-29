namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Partição dedicada que hospeda a ISO durante a instalação. Existe porque a ISO NÃO pode
    /// morar numa partição do usuário: o casper monta a partição hospedeira em
    /// <c>/isodevice</c> read-write e nunca a solta, então no modo substituir o curtin não
    /// consegue liberá-la e a instalação aborta no <c>clear-holders</c> (erro real,
    /// 2026-07-29). Numa partição própria, ela é declarada como preservada no storage config e
    /// o instalador não encosta nela.
    ///
    /// De quebra resolve o BitLocker: partição criada aqui nunca é criptografada, então o GRUB
    /// consegue ler a ISO mesmo com o volume do Windows cifrado.
    /// </summary>
    public interface IStagingPartitionService
    {
        /// <summary>Espaço não alocado que a partição precisa para caber a ISO indicada.
        /// Consultado ANTES de qualquer escrita, para a pré-checagem poder recusar sem ter
        /// criado nada.</summary>
        long RequiredBytesFor(long isoSizeInBytes);

        /// <summary>Cria e formata a partição no espaço não alocado do disco. Quem chama
        /// precisa ter garantido o espaço antes.</summary>
        StagingPartition Create(int diskIndex, long isoSizeInBytes);

        /// <summary>Copia a ISO para a partição e confere que a cópia saiu íntegra. Uma cópia
        /// truncada aborta — prosseguir com ela produziria um boot que falha depois do reboot,
        /// quando não há mais como avisar o usuário.</summary>
        void CopyIso(StagingPartition partition, string isoSourcePath, IProgress<string>? progress);
    }

    /// <summary>
    /// Identidade da partição de staging. O PARTUUID é o que permite removê-la depois com
    /// segurança, do sistema já instalado — índice de partição não serve, porque o instalador
    /// reescreve a tabela e os números mudam.
    /// </summary>
    public sealed record StagingPartition(int DiskIndex, int PartitionNumber, string PartitionUuid);
}
