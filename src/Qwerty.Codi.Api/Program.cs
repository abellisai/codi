using System.Text.Json;
using Qwerty.Codi.Api.Authentication;
using Qwerty.Codi.Api.Configuration;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Infrastructure.Persistence;

// Fondazioni di T1 (§21.1): configurazione, migrazioni, dispatcher CQRS, accesso amministrativo
// e salute del processo. Dalla prima porzione di T3 (§21.1) sono disponibili l'istanziazione
// (`CreateSession`) e l'avvio (`StartSession`) della sessione di lezione.
var builder = WebApplication.CreateBuilder(args);

// §20.2: la composizione vive in un punto solo, condiviso con i test di composizione.
builder.Services.AddCodiApi(builder.Configuration, builder.Environment);

// Le durate delle dipendenze sono un difetto silenzioso: un servizio con durata maggiore che
// dipende da uno con durata minore si scopre alla prima richiesta che lo risolve, se va bene.
// Con la validazione l'errore arriva all'avvio e nomina servizio e durata. In Development il
// provider predefinito la attiva di suo; qui la si chiede in tutti gli ambienti, laboratorio
// compreso, perché è l'ambiente in cui nessuno legge i log con attenzione.
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateOnBuild = true;
    options.ValidateScopes = true;
});

var app = builder.Build();

// Il gestore delle eccezioni sta prima degli endpoint: gli errori applicativi di §10.7 diventano
// Problem Details con codice stabile, il resto resta un 500 senza dettagli interni. Poi
// l'autenticazione popola l'identità dal cookie, che è ciò da cui il dispatcher ricava il contesto.
app.UseCodiApi();

// §4.4: il registro rifiuta l'avvio se una richiesta non dichiara una policy, se due richieste
// condividono lo stesso nome o se l'accesso anonimo non corrisponde all'elenco chiuso di §10.4.
// Si risolve qui, prima dell'ascolto, e non alla prima richiesta.
var registry = app.Services.GetRequiredService<CodiRequestRegistry>();
app.Logger.LogInformation(
    "Registro delle richieste: {CommandCount} comandi, {QueryCount} query, {PendingAnonymousCount} richieste anonime non ancora implementate.",
    registry.Descriptors.Count(descriptor => descriptor.Kind == CodiRequestKind.Command),
    registry.Descriptors.Count(descriptor => descriptor.Kind == CodiRequestKind.Query),
    registry.MissingAnonymousBootstrapRequests.Count);

// §16.1: prima della readiness si verificano configurazione, accesso ai dati e migrazioni.
// La preparazione avviene prima che il server accetti richieste: uno schema non allineato o un
// journal diverso da WAL impediscono l'avvio invece di degradare le richieste successive.
var preparation = await app.Services
    .GetRequiredService<DatabaseBootstrap>()
    .PrepareAsync(app.Lifetime.ApplicationStopping);
app.Logger.LogInformation(
    "Database pronto: journal_mode {JournalMode}, schema {SchemaVersion}.",
    preparation.JournalMode,
    preparation.SchemaVersion);

// Opzione che attiva la modalità locale di §9.1.
const string BootstrapOption = "--bootstrap-admin";

// §9.1: il primo account amministrativo si crea da riga di comando locale, con la password letta
// da un secret temporaneo e mai dalla riga di comando, dove resterebbe nella cronologia della
// shell. La modalità locale esce senza mettere in ascolto il servizio.
if (args.Length >= 1 && string.Equals(args[0], BootstrapOption, StringComparison.Ordinal))
{
    if (args.Length < 2)
    {
        app.Logger.LogError("Uso: {Option} <nome utente>. Il nome è un'etichetta, non un segreto.", BootstrapOption);

        return 1;
    }

    return await BootstrapAdminAccountAsync(app, args[1], app.Lifetime.ApplicationStopping);
}

// §9.1: se l'ambiente imposta Codi:SeedAdmin, qui si crea il primo account amministrativo. Il
// database è già preparato e l'inserimento passa dal caso d'uso del comando locale, che rifiuta
// un secondo account: a ogni avvio successivo al primo non c'è niente da fare. Un ambiente che
// non configura la sezione non crea alcuna credenziale.
using (var seedScope = app.Services.CreateScope())
{
    try
    {
        await seedScope.ServiceProvider
            .GetRequiredService<AdminAccountSeed>()
            .EnsureAsync(app.Lifetime.ApplicationStopping);
    }
    catch (CodiRequestException exception)
    {
        // Un account iniziale mal configurato è un errore di configurazione: ferma l'avvio invece
        // di lasciare il servizio in ascolto senza l'accesso che l'ambiente si aspetta.
        app.Logger.LogError(
            "Account amministrativo iniziale non creato: {ErrorCode}. {Message}",
            exception.Code,
            exception.Message);

        return 1;
    }
}

using (var recoveryScope = app.Services.CreateScope())
{
    var recovered = await recoveryScope.ServiceProvider
        .GetRequiredService<Qwerty.Codi.Application.Sessions.SessionStartupRecovery>()
        .RecoverAsync(app.Lifetime.ApplicationStopping);
    app.Logger.LogInformation("Recupero iniziale completato: {SessionCount} sessioni non terminali.", recovered);
}

app.Run();

return 0;

/// <summary>Crea il primo account amministrativo e restituisce l'esito al terminale.</summary>
static async Task<int> BootstrapAdminAccountAsync(
    WebApplication application,
    string username,
    CancellationToken cancellationToken)
{
    const string PasswordVariable = "CODI_BOOTSTRAP_PASSWORD";

    var password = Environment.GetEnvironmentVariable(PasswordVariable);
    if (string.IsNullOrEmpty(password))
    {
        application.Logger.LogError(
            "Impostare {PasswordVariable} con la password del primo amministratore: la riga di comando non è il posto giusto per un segreto.",
            PasswordVariable);

        return 1;
    }

    using var scope = application.Services.CreateScope();
    var executor = scope.ServiceProvider.GetRequiredService<CodiRequestExecutor>();

    // Il payload passa dal formato dei corpi del dispatcher: il percorso locale e quello HTTP
    // non devono differire per come i dati arrivano al caso d'uso.
    using var payload = JsonDocument.Parse(
        JsonSerializer.Serialize(new { username, password }, CodiJson.Payload));

    try
    {
        var created = await executor.ExecuteAsync(
            CodiRequestKind.Command,
            AnonymousBootstrapRequests.BootstrapAdminAccount,
            CodiRequestChannel.Local,
            payload.RootElement,
            CodiRequestContext.Anonymous,
            cancellationToken);

        application.Logger.LogInformation("Account amministrativo creato: {Created}", created);

        return 0;
    }
    catch (CodiRequestException exception)
    {
        application.Logger.LogError(
            "Bootstrap non eseguito: {ErrorCode}. {Message}",
            exception.Code,
            exception.Message);

        return 1;
    }
}
