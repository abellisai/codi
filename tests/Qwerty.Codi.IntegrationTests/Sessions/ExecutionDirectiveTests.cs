using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Sessions;

/// <summary>
/// Direttiva di esecuzione persistita (§6.2): il documento lo scrive il server, quindi la lettura
/// non corregge un documento illeggibile né inventa un tipo — lo dichiara come difetto interno,
/// perché una direttiva interpretata male farebbe ripartire il lavoro di un alunno.
/// </summary>
public sealed class ExecutionDirectiveTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Read_SenzaDirettivaPersistita_RestituisceNull()
    {
        Assert.Null(ExecutionDirectiveDto.Read(null));
        Assert.Null(ExecutionDirectiveDto.Read("   "));
    }

    [Fact]
    public void Read_ConDirettivaDiRiavvio_RileggeGliStessiValori()
    {
        var written = ExecutionDirectiveDto.Restart(executionGeneration: 3, Now);

        var read = ExecutionDirectiveDto.Read(written.ToJson());

        Assert.NotNull(read);
        Assert.Equal(written.DirectiveId, read!.DirectiveId);
        Assert.Equal(ExecutionDirectiveDto.RestartKind, read.Kind);
        Assert.Equal(3L, read.ExecutionGeneration);
        Assert.Equal(Now, read.IssuedAtUtc);
    }

    [Fact]
    public void Read_ConDirettivaSenzaGenerazione_LaLasciaAssente()
    {
        var written = ExecutionDirectiveDto.Run(executionGeneration: null, Now);

        var read = ExecutionDirectiveDto.Read(written.ToJson());

        Assert.NotNull(read);
        Assert.Equal(ExecutionDirectiveDto.RunKind, read!.Kind);
        Assert.Null(read.ExecutionGeneration);
    }

    [Fact]
    public void Read_ConDocumentoIllegibile_Rifiuta()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => ExecutionDirectiveDto.Read("non-un-documento"));

        Assert.Contains("JSON leggibile", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ConTipoNonRiconosciuto_Rifiuta()
    {
        const string DirectiveJson = """{"directiveId":"11111111-1111-1111-1111-111111111111","kind":"Fly","executionGeneration":null,"issuedAtUtc":"2026-10-02T09:00:00+00:00"}""";

        var failure = Assert.Throws<InvalidOperationException>(() => ExecutionDirectiveDto.Read(DirectiveJson));

        Assert.Contains("tipo riconosciuto", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ConDocumentoIncompleto_Rifiuta()
    {
        // I campi obbligatori del record mancano: il documento non è una direttiva, è un difetto.
        var failure = Assert.Throws<InvalidOperationException>(() => ExecutionDirectiveDto.Read("{}"));

        Assert.Contains("JSON leggibile", failure.Message, StringComparison.Ordinal);
    }
}
