using LinuxHub.Common.Data;

// Ferramenta de release (task 7.7), não código de app: gera o documento de catálogo assinável
// a partir do MESMO catálogo embarcado no executável (DistroCatalog.Fallback), usando o mesmo
// escritor que o teste de paridade de schema (SchemaValidationTests) exercita. Nunca reimplementa
// a forma do documento — só chama CatalogDocumentWriter, que é a única fonte de verdade.
//
// Não assina nada. A assinatura acontece num passo separado do pipeline de release
// (Scripts/sign-catalog-document.ps1), rodando só no CI — nunca aqui, para que a chave privada
// nunca precise existir onde este processo roda.
if (args.Length != 1)
{
    Console.Error.WriteLine("Uso: CatalogPublisher <caminho-de-saida.json>");
    return 1;
}

string outputPath = args[0];
string json = CatalogDocumentWriter.BuildJson(DistroCatalog.Fallback);

File.WriteAllText(outputPath, json);
Console.WriteLine($"Catálogo gerado: {outputPath} ({DistroCatalog.Fallback.Count} distros)");
return 0;
