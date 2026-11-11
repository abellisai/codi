using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Dispatch;
using Qwerty.Codi.Application.Dto;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Infrastructure.Realtime;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Coordinatore delle sessioni (§8.2): due comandi sulla stessa lezione si mettono in fila, due
/// lezioni diverse no. È la regola che rende utile la revisione attesa — senza, due comandi
/// concorrenti leggono la stessa revisione e il secondo fallisce con un conflitto che il
/// coordinamento evita.
/// </summary>
public sealed class SessionCoordinatorTests
{
    private readonly ISessionCoordinator _coordinator = new ServiceCollection()
        .AddCodiRealtimeInfrastructure()
        .BuildServiceProvider()
        .GetRequiredService<ISessionCoordinator>();

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Attesa_SullaStessaSessione_NonConcedeIlPermessoFincheIlPrimoNonRilascia()
    {
        var sessionId = Guid.NewGuid();

        await using var first = await _coordinator.AcquireAsync(sessionId);

        var second = _coordinator.AcquireAsync(sessionId);
        Assert.False(second.IsCompleted);

        await first.DisposeAsync();

        var acquired = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(acquired);
        await acquired.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Sessioni_Diverse_NonSiAspettano()
    {
        await using var first = await _coordinator.AcquireAsync(Guid.NewGuid());

        var other = await _coordinator.AcquireAsync(Guid.NewGuid()).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(other);
        await other.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task AttesaAnnullata_RilasciaIlPostoSenzaTrattenereLaSessione()
    {
        var sessionId = Guid.NewGuid();
        await using var held = await _coordinator.AcquireAsync(sessionId);

        using var cancellation = new CancellationTokenSource();
        var waiting = _coordinator.AcquireAsync(sessionId, cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        // Il permesso non è stato consumato: chi arriva dopo non resta in attesa di un posto che
        // nessuno rilascerà.
        await held.DisposeAsync();

        var after = await _coordinator.AcquireAsync(sessionId).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(after);
        await after.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Attesa_SenzaSessione_Rifiuta()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _coordinator.AcquireAsync(Guid.Empty));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DueComandi_Concorrenti_SullaStessaRevisione_NePassaUnoSolo()
    {
        // §8.2: il coordinamento si acquisisce prima della transazione, quindi il secondo comando
        // legge la revisione già avanzata dal primo e la rifiuta con un conflitto previsto, invece di
        // provare a scrivere una seconda volta la stessa revisione.
        await using var scenario = await OutboxScenario.CreateAsync();
        var seed = await scenario.StartedSessionAsync();
        var context = scenario.RegiaContext();

        var first = scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            ShowWaitingCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = 2L },
            context);

        var second = scenario.Host.CommandAsAsync<CommandAcceptedDto>(
            ShowWaitingCommand.RequestName,
            new { sessionId = seed.SessionId, expectedSessionRevision = 2L },
            context);

        var outcomes = await Task.WhenAll(
            CaptureAsync(first),
            CaptureAsync(second));

        var accepted = Assert.Single(outcomes, outcome => outcome.Accepted);
        Assert.Equal(3L, accepted.Revision);
        Assert.Equal(CodiErrorCodes.SessionRevisionConflict, Assert.Single(outcomes, outcome => !outcome.Accepted).Code);
    }

    /// <summary>Esegue una richiesta e riporta l'esito come dato, senza propagare l'eccezione.</summary>
    private static async Task<(bool Accepted, long Revision, string? Code)> CaptureAsync(Task<CommandAcceptedDto> request)
    {
        try
        {
            var accepted = await request;

            return (true, accepted.SessionRevision, null);
        }
        catch (CodiRequestException rejection)
        {
            return (false, 0, rejection.Code);
        }
    }
}
