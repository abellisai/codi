using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Qwerty.Codi.Application.Dispatch;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Organization;

/// <summary>
/// Percorso HTTP dell'organizzazione (§10.1, §10.2, §10.7): gli stessi casi d'uso visti dal
/// trasporto, con i loro stati. È il livello che verifica ciò che i test in-process non possono
/// vedere: il codice dell'errore tradotto nello stato HTTP e il token antiforgery richiesto.
/// </summary>
public sealed class SchoolHttpTests : IAsyncLifetime
{
    private CodiHttpServer _server = null!;

    public async Task InitializeAsync()
    {
        _server = await CodiHttpServer.CreateAsync();

        using var login = await _server.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CicloCompleto_CreazioneElencoModificaEArchiviazione_RispettaGliStati()
    {
        // Act: creazione
        using var created = await _server.SendAsync(
            HttpMethod.Post, "/api/v1/admin/execute/command/CreateSchool", new { name = "Scuola Manzoni" });

        // Assert: il dispatcher restituisce l'esito del caso d'uso, con la versione da rimandare.
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var school = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = school.GetProperty("id").GetGuid();
        Assert.Equal("Scuola Manzoni", school.GetProperty("name").GetString());
        Assert.Equal("1", school.GetProperty("rowVersion").GetString());

        // Act / Assert: la query vede la scuola appena creata.
        using var listed = await _server.SendAsync(
            HttpMethod.Post, "/api/v1/admin/execute/query/ListSchools", new { includeArchived = false });
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var schools = await listed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Scuola Manzoni", Assert.Single(schools.EnumerateArray()).GetProperty("name").GetString());

        // Act / Assert: la modifica avanza la versione di riga.
        using var renamed = await _server.SendAsync(
            HttpMethod.Post,
            "/api/v1/admin/execute/command/UpdateSchool",
            new { id, name = "Scuola Manzoni Due", baseRowVersion = "1" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var updated = await renamed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2", updated.GetProperty("rowVersion").GetString());

        // Act / Assert: l'archiviazione toglie la scuola dall'elenco ordinario.
        using var archived = await _server.SendAsync(
            HttpMethod.Post,
            "/api/v1/admin/execute/command/ArchiveSchool",
            new { id, baseRowVersion = "2" });
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.True((await archived.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isArchived").GetBoolean());

        using var empty = await _server.SendAsync(
            HttpMethod.Post, "/api/v1/admin/execute/query/ListSchools", new { includeArchived = false });
        Assert.Empty((await empty.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Dispatcher_SenzaCookie_RifiutaComeAccessoRichiesto()
    {
        // Act: la policy Admin precede l'handler, quindi il rifiuto non dipende dal corpo.
        using var response = await _server.SendAnonymousAsync(
            HttpMethod.Post, "/api/v1/admin/execute/query/ListSchools", new { });

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CodiErrorCodes.AuthenticationRequired, problem.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("Scuola Manzoni", "forcellini")]
    [InlineData("forcellini", "Forcellini")]
    [InlineData("forcellini", "Forcellìni")]
    [Trait("Category", "Integration")]
    public async Task UpdateSchool_ConNuovoNome_RestituisceEPersistisceIlNomeDigitato(string originalName, string newName)
    {
        // Arrange: un database temporaneo e una scuola realmente creata attraverso HTTP.
        using var created = await _server.SendAsync(HttpMethod.Post,
            "/api/v1/admin/execute/command/CreateSchool", new { name = originalName });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var school = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = school.GetProperty("id").GetGuid();

        // Act: salva e poi rilegge da un'altra richiesta, senza fidarsi soltanto del 200.
        using var renamed = await _server.SendAsync(HttpMethod.Post,
            "/api/v1/admin/execute/command/UpdateSchool",
            new { id, name = newName, baseRowVersion = school.GetProperty("rowVersion").GetString() });
        using var reread = await _server.SendAsync(HttpMethod.Post,
            "/api/v1/admin/execute/query/GetSchool", new { id });
        using var listed = await _server.SendAsync(HttpMethod.Post,
            "/api/v1/admin/execute/query/ListSchools", new { includeArchived = false });

        // Assert: DTO della mutazione, dettaglio e lista concordano sul nome persistito.
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reread.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var updated = await renamed.Content.ReadFromJsonAsync<JsonElement>();
        var stored = await reread.Content.ReadFromJsonAsync<JsonElement>();
        var entry = Assert.Single((await listed.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        foreach (var result in new[] { updated, stored, entry })
        {
            Assert.Equal(id, result.GetProperty("id").GetGuid());
            Assert.Equal(newName, result.GetProperty("name").GetString());
            Assert.Equal("2", result.GetProperty("rowVersion").GetString());
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateSchool_ConNomeGiaInUso_RispondeConConflittoECodice()
    {
        // Arrange
        using var first = await _server.SendAsync(
            HttpMethod.Post, "/api/v1/admin/execute/command/CreateSchool", new { name = "Scuola Manzoni" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Act
        using var duplicate = await _server.SendAsync(
            HttpMethod.Post, "/api/v1/admin/execute/command/CreateSchool", new { name = "  SCUOLA MANZÒNI  " });

        // Assert: 409 con il codice del contratto, non un 500.
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var problem = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CodiErrorCodes.SchoolAlreadyExists, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateSchool_SenzaVersione_RispondeCheLaVersioneEObbligatoria()
    {
        // Act
        using var response = await _server.SendAsync(
            HttpMethod.Post,
            "/api/v1/admin/execute/command/UpdateSchool",
            new { id = Guid.NewGuid(), name = "Scuola Manzoni Due" });

        // Assert: §10.2 vuole 428 quando la versione è obbligatoria e assente.
        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CodiErrorCodes.EntityVersionRequired, problem.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UpdateSchool_ConVersioneObsoleta_RispondeConPrecondizioneFallita()
    {
        // Arrange
        using var created = await _server.SendAsync(
            HttpMethod.Post, "/api/v1/admin/execute/command/CreateSchool", new { name = "Scuola Manzoni" });
        var school = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = school.GetProperty("id").GetGuid();

        // Act: la versione dichiarata non è quella memorizzata.
        using var stale = await _server.SendAsync(
            HttpMethod.Post,
            "/api/v1/admin/execute/command/UpdateSchool",
            new { id, name = "Scuola Manzoni Due", baseRowVersion = "7" });

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        var problem = await stale.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CodiErrorCodes.RowVersionMismatch, problem.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetSchool_ConIdentificativoInesistente_RispondeRisorsaNonTrovata()
    {
        // Act
        using var response = await _server.SendAsync(
            HttpMethod.Post, "/api/v1/admin/execute/query/GetSchool", new { id = Guid.NewGuid() });

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CodiErrorCodes.ResourceNotFound, problem.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Mutazione_SenzaTokenAntiforgery_RispondeConRifiutoApplicativo()
    {
        // Act: il cookie autenticato c'è, il token no.
        using var response = await _server.SendAsync(
            HttpMethod.Post,
            "/api/v1/admin/execute/command/CreateSchool",
            new { name = "Scuola Manzoni" },
            header: null,
            useCookieToken: false);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(CodiErrorCodes.AntiforgeryRejected, problem.GetProperty("code").GetString());
    }
}
