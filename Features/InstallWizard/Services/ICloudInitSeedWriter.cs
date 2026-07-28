namespace LinuxHub.Features.InstallWizard.Services
{
    /// <summary>
    /// Duas etapas em vez de uma porque o <c>user-data</c> descreve o disco COM a partição de
    /// semente já nele: o storage config precisa declarar todas as partições existentes, e
    /// esta é uma delas. Então a partição nasce primeiro, o layout real é lido depois, e só
    /// então há um YAML para gravar.
    /// </summary>
    public interface ICloudInitSeedWriter
    {
        /// <summary>Cria e formata a partição de semente no espaço não alocado do disco.
        /// Devolve o número dela.</summary>
        int CreateSeedPartition(int diskIndex);

        void WriteSeedFiles(int diskIndex, int partitionNumber, string userData, string metaData);
    }
}
