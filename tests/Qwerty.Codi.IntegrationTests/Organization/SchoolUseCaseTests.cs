using Qwerty.Codi.Application.Dto;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Organization;
using Qwerty.Codi.Infrastructure.Migrations;
using Qwerty.Codi.Infrastructure.Persistence;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Organization;

/// <summary>
/// Casi d'uso dell'organizzazione (§10.4, §10.5) attraverso il percorso reale: registro, canale,
/// policy, deserializzazione, mediator generato in <c>Application</c>, handler, repository e
/// database migrato. Verifica ciò che il test del repository non può vedere: policy controllata
/// prima dell'handler, versione di riga richiesta e conflitti con il loro codice.
/// </summary>
public sealed class SchoolUseCaseTests : IAsyncLifetime
{
    private TempSqliteDatabase _database = null!;

    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _database = TempSqliteDatabase.Create();

        var runner = new MigrationRunner(_database.Factory, new TestTimeProvider());
        await runner.ApplyAsync(
            MigrationCatalog.FromEmbeddedScripts(typeof(MigrationCatalog).Assembly),
            CancellationToken.None);

        var services = new ServiceCollection();
        services.AddCodiRequests();
        services.AddSingleton(_database.Factory);
        services.AddCodiPersistence();
        services.AddSingleton<TimeProvider>(new TestTimeProvider());

        _services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        _database.Dispose();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CreateSchool_CreaLaScuolaVisibileNellElenco()
    {
        // Arrange
        var executor = CreateExecutor();
        using var payload = Payload(new { name = "  Scuola   Manzoni " });

        // Act
        var result = await executor.ExecuteAsync(
            CodiRequestKind.Command,
            CreateSchoolCommand.RequestName,
            CodiRequestChannel.Http,
            payload.RootElement,
            AdminContext(),
            CancellationToken.None);

        // Assert
        var created = Assert.IsType<SchoolDto>(result);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Scuola   Manzoni", created.Name);
        Assert.False(created.IsArchived);
        Assert.Equal(1, created.RowVersion);

        var stored = await ListAsync(executor);
        Assert.Equal(created, Assert.Single(stored));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CreateSchoolConNomeEquivalente_RifiutaSenzaCreareLaSeconda()
    {
        // Arrange: due scritture dello stesso nome normalizzato sono la stessa scuola (§7.2).
        var executor = CreateExecutor();
        await CreateSchoolAsync(executor, "Scuola Manzoni");

        using var payload = Payload(new { name = "  SCUOLA   MANZÒNI  " });

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                CreateSchoolCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                AdminContext(),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.SchoolAlreadyExists, exception.Code);
        Assert.Single(await ListAsync(executor));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_CreateSchoolConNomeInutilizzabile_RifiutaConIlCodiceDelNome()
    {
        // Arrange
        var executor = CreateExecutor();
        using var payload = Payload(new { name = "   " });

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                CreateSchoolCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                AdminContext(),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidName, exception.Code);
        Assert.Empty(await ListAsync(executor));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateSchool_CambiaIlNomeEMantieneLaScuola()
    {
        // Arrange
        var executor = CreateExecutor();
        var created = await CreateSchoolAsync(executor, "Scuola Manzoni");
        using var payload = Payload(new
        {
            id = created.Id,
            name = "Scuola Manzoni Due",
            baseRowVersion = created.RowVersion,
        });

        // Act
        var result = await executor.ExecuteAsync(
            CodiRequestKind.Command,
            UpdateSchoolCommand.RequestName,
            CodiRequestChannel.Http,
            payload.RootElement,
            AdminContext(),
            CancellationToken.None);

        // Assert
        var updated = Assert.IsType<SchoolDto>(result);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Scuola Manzoni Due", updated.Name);
        Assert.Equal(2, updated.RowVersion);

        using var readBack = Payload(new { id = created.Id });
        var stored = Assert.IsType<SchoolDto>(await executor.ExecuteAsync(
            CodiRequestKind.Query,
            GetSchoolQuery.RequestName,
            CodiRequestChannel.Http,
            readBack.RootElement,
            AdminContext(),
            CancellationToken.None));
        Assert.Equal(updated, stored);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateSchoolSenzaVersione_RichiedeLaVersioneAttesa()
    {
        // Arrange: §10.2 chiede 428 quando la versione è obbligatoria e assente, non un corpo non
        // deserializzabile: sono due esiti diversi per il client.
        var executor = CreateExecutor();
        var created = await CreateSchoolAsync(executor, "Scuola Manzoni");
        using var payload = Payload(new { id = created.Id, name = "Scuola Manzoni Due" });

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                UpdateSchoolCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                AdminContext(),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, exception.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateSchoolConVersioneObsoleta_NonSovrascrive()
    {
        // Arrange: la seconda richiesta porta la versione letta prima della prima modifica.
        var executor = CreateExecutor();
        var created = await CreateSchoolAsync(executor, "Scuola Manzoni");
        await UpdateSchoolAsync(executor, created.Id, "Scuola Manzoni Prima", created.RowVersion);

        using var payload = Payload(new
        {
            id = created.Id,
            name = "Scuola Manzoni Seconda",
            baseRowVersion = created.RowVersion,
        });

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                UpdateSchoolCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                AdminContext(),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, exception.Code);

        var stored = await GetAsync(executor, created.Id);
        Assert.Equal("Scuola Manzoni Prima", stored.Name);
        Assert.Equal(2, stored.RowVersion);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateSchoolConNomeGiaInUso_RifiutaSenzaModificare()
    {
        // Arrange
        var executor = CreateExecutor();
        await CreateSchoolAsync(executor, "Scuola Manzoni");
        var second = await CreateSchoolAsync(executor, "Scuola Dante");

        using var payload = Payload(new
        {
            id = second.Id,
            name = "Scuola Manzoni",
            baseRowVersion = second.RowVersion,
        });

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                UpdateSchoolCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                AdminContext(),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.SchoolAlreadyExists, exception.Code);

        var stored = await GetAsync(executor, second.Id);
        Assert.Equal("Scuola Dante", stored.Name);
        Assert.Equal(1, stored.RowVersion);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateSchoolConLoStessoNome_NonAlzaLaVersione()
    {
        // Arrange
        var executor = CreateExecutor();
        var created = await CreateSchoolAsync(executor, "Scuola Manzoni");
        using var payload = Payload(new
        {
            id = created.Id,
            name = "  Scuola Manzoni  ",
            baseRowVersion = created.RowVersion,
        });

        // Act
        var result = await executor.ExecuteAsync(
            CodiRequestKind.Command,
            UpdateSchoolCommand.RequestName,
            CodiRequestChannel.Http,
            payload.RootElement,
            AdminContext(),
            CancellationToken.None);

        // Assert
        var unchanged = Assert.IsType<SchoolDto>(result);
        Assert.Equal("Scuola Manzoni", unchanged.Name);
        Assert.Equal(1, unchanged.RowVersion);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ArchiveSchool_ToglieLaScuolaDallElencoPredefinitoMaRestaLeggibile()
    {
        // Arrange
        var executor = CreateExecutor();
        var created = await CreateSchoolAsync(executor, "Scuola Manzoni");
        using var payload = Payload(new { id = created.Id, baseRowVersion = created.RowVersion });

        // Act
        var result = await executor.ExecuteAsync(
            CodiRequestKind.Command,
            ArchiveSchoolCommand.RequestName,
            CodiRequestChannel.Http,
            payload.RootElement,
            AdminContext(),
            CancellationToken.None);

        // Assert
        var archived = Assert.IsType<SchoolDto>(result);
        Assert.True(archived.IsArchived);
        Assert.Equal(2, archived.RowVersion);

        Assert.Empty(await ListAsync(executor));
        Assert.True((await GetAsync(executor, created.Id)).IsArchived);
        Assert.Single(await ListAsync(executor, includeArchived: true));

        // Archiviare di nuovo è un esito previsto: lo stato è già quello richiesto e la versione
        // non cresce per una richiesta che non cambia nulla.
        using var repeated = Payload(new { id = created.Id, baseRowVersion = archived.RowVersion });
        var again = Assert.IsType<SchoolDto>(await executor.ExecuteAsync(
            CodiRequestKind.Command,
            ArchiveSchoolCommand.RequestName,
            CodiRequestChannel.Http,
            repeated.RootElement,
            AdminContext(),
            CancellationToken.None));
        Assert.True(again.IsArchived);
        Assert.Equal(2, again.RowVersion);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ListSchools_OrdinaPerNomeSenzaDistinguereLeMaiuscole()
    {
        // Arrange
        var executor = CreateExecutor();
        await CreateSchoolAsync(executor, "scuola zeta");
        await CreateSchoolAsync(executor, "Scuola Alfa");

        // Act
        var schools = await ListAsync(executor);

        // Assert
        Assert.Equal(
            new[] { "Scuola Alfa", "scuola zeta" },
            schools.Select(school => school.Name));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_UpdateSchoolConNomeInutilizzabile_RifiutaConIlCodiceDelNome()
    {
        // Arrange
        var executor = CreateExecutor();
        var created = await CreateSchoolAsync(executor, "Scuola Manzoni");
        using var payload = Payload(new
        {
            id = created.Id,
            name = "   ",
            baseRowVersion = created.RowVersion,
        });

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                UpdateSchoolCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                AdminContext(),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidName, exception.Code);
        Assert.Equal("Scuola Manzoni", (await GetAsync(executor, created.Id)).Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ArchiveSchoolSenzaVersione_RichiedeLaVersioneAttesa()
    {
        // Arrange
        var executor = CreateExecutor();
        var created = await CreateSchoolAsync(executor, "Scuola Manzoni");
        using var payload = Payload(new { id = created.Id });

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                ArchiveSchoolCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                AdminContext(),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, exception.Code);
        Assert.False((await GetAsync(executor, created.Id)).IsArchived);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ArchiveSchoolConVersioneObsoleta_NonArchivia()
    {
        // Arrange
        var executor = CreateExecutor();
        var created = await CreateSchoolAsync(executor, "Scuola Manzoni");
        await UpdateSchoolAsync(executor, created.Id, "Scuola Manzoni Due", created.RowVersion);
        using var payload = Payload(new { id = created.Id, baseRowVersion = created.RowVersion });

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                ArchiveSchoolCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                AdminContext(),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, exception.Code);
        Assert.False((await GetAsync(executor, created.Id)).IsArchived);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_SuIdentificativoInesistente_RestituisceRisorsaNonTrovata()
    {
        // Arrange
        var executor = CreateExecutor();
        using var payload = Payload(new { id = Guid.NewGuid(), baseRowVersion = 1 });

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                ArchiveSchoolCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                AdminContext(),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.ResourceNotFound, exception.Code);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConContestoAnonimo_RifiutaPrimaDiEseguire()
    {
        // Arrange: la policy si controlla prima dell'handler, quindi la scuola non nasce.
        var executor = CreateExecutor();
        using var payload = Payload(new { name = "Scuola Manzoni" });

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                CreateSchoolCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                CodiRequestContext.Anonymous,
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.AuthenticationRequired, exception.Code);
        Assert.Empty(await ListAsync(executor));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConContestoAutenticatoSenzaPolicyAdmin_RifiutaConPermessoMancante()
    {
        // Arrange: un partecipante autenticato non gestisce l'organizzazione (§9.3).
        var executor = CreateExecutor();
        using var payload = Payload(new { name = "Scuola Manzoni" });
        var participantContext = new CodiRequestContext(isAuthenticated: true, [CodiPolicy.AdmittedParticipant]);

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                CreateSchoolCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                participantContext,
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.PermissionDenied, exception.Code);
        Assert.Empty(await ListAsync(executor));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_ConCorpoIncompleto_RifiutaIlPayloadPrimaDellHandler()
    {
        // Arrange: il nome è obbligatorio nel record, quindi `{}` non è un nome vuoto da validare.
        var executor = CreateExecutor();
        using var payload = JsonDocument.Parse("{}");

        // Act
        var exception = await Assert.ThrowsAsync<CodiRequestException>(() => executor
            .ExecuteAsync(
                CodiRequestKind.Command,
                CreateSchoolCommand.RequestName,
                CodiRequestChannel.Http,
                payload.RootElement,
                AdminContext(),
                CancellationToken.None)
            .AsTask());

        // Assert
        Assert.Equal(CodiErrorCodes.InvalidPayload, exception.Code);
    }

    private static CodiRequestContext AdminContext() => new(isAuthenticated: true, [CodiPolicy.Admin]);

    private CodiRequestExecutor CreateExecutor()
    {
        var scope = _services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<CodiRequestExecutor>();
    }

    private static JsonDocument Payload(object body) =>
        JsonDocument.Parse(JsonSerializer.Serialize(body, CodiJson.Payload));

    private static async Task<SchoolDto> CreateSchoolAsync(CodiRequestExecutor executor, string name)
    {
        using var payload = Payload(new { name });

        return Assert.IsType<SchoolDto>(await executor.ExecuteAsync(
            CodiRequestKind.Command,
            CreateSchoolCommand.RequestName,
            CodiRequestChannel.Http,
            payload.RootElement,
            AdminContext(),
            CancellationToken.None));
    }

    private static async Task<SchoolDto> UpdateSchoolAsync(
        CodiRequestExecutor executor, Guid id, string name, long baseRowVersion)
    {
        using var payload = Payload(new { id, name, baseRowVersion });

        return Assert.IsType<SchoolDto>(await executor.ExecuteAsync(
            CodiRequestKind.Command,
            UpdateSchoolCommand.RequestName,
            CodiRequestChannel.Http,
            payload.RootElement,
            AdminContext(),
            CancellationToken.None));
    }

    private static async Task<SchoolDto> GetAsync(CodiRequestExecutor executor, Guid id)
    {
        using var payload = Payload(new { id });

        return Assert.IsType<SchoolDto>(await executor.ExecuteAsync(
            CodiRequestKind.Query,
            GetSchoolQuery.RequestName,
            CodiRequestChannel.Http,
            payload.RootElement,
            AdminContext(),
            CancellationToken.None));
    }

    private static async Task<IReadOnlyList<SchoolDto>> ListAsync(
        CodiRequestExecutor executor, bool includeArchived = false)
    {
        using var payload = Payload(new { includeArchived });

        return Assert.IsAssignableFrom<IReadOnlyList<SchoolDto>>(await executor.ExecuteAsync(
            CodiRequestKind.Query,
            ListSchoolsQuery.RequestName,
            CodiRequestChannel.Http,
            payload.RootElement,
            AdminContext(),
            CancellationToken.None));
    }
}
