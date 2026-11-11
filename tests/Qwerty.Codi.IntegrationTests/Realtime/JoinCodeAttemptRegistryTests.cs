using Microsoft.Extensions.DependencyInjection;
using Qwerty.Codi.Application.Abstractions;
using Qwerty.Codi.Application.Sessions;
using Qwerty.Codi.Infrastructure.Realtime;
using Xunit;

namespace Qwerty.Codi.IntegrationTests.Realtime;

/// <summary>
/// Regole del conteggio dei tentativi di codice di ingresso (§9.4, §17.3): quando un browser è
/// sospeso, quando il blocco cade, che cosa azzera un ingresso riuscito e perché il conteggio
/// dell'indirizzo di rete non si elude rinnovando il ticket. Sono regole di tempo, quindi il tempo
/// è dichiarato dai casi e non aspettato.
/// </summary>
public sealed class JoinCodeAttemptRegistryTests
{
    private static readonly DateTimeOffset Now = TestTimeProvider.Instant;

    private readonly IJoinCodeAttemptRegistry _registry;

    public JoinCodeAttemptRegistryTests()
    {
        var services = new ServiceCollection();
        services.AddCodiRealtimeInfrastructure();

        _registry = services.BuildServiceProvider().GetRequiredService<IJoinCodeAttemptRegistry>();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void SottoLaSoglia_NonBlocca()
    {
        var scope = Scope();

        for (var attempt = 0; attempt < JoinCodeAttemptPolicy.MaximumFailures - 1; attempt++)
        {
            _registry.RegisterFailure(scope, Now);
        }

        Assert.False(_registry.IsBlocked(scope, Now));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void AllaSoglia_BloccaIlBrowser()
    {
        var scope = Scope();

        for (var attempt = 0; attempt < JoinCodeAttemptPolicy.MaximumFailures; attempt++)
        {
            _registry.RegisterFailure(scope, Now);
        }

        Assert.True(_registry.IsBlocked(scope, Now));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void TrascorsaLaFinestra_IlBloccoCade()
    {
        var scope = BlockedScope();

        Assert.True(_registry.IsBlocked(scope, Now));

        // Il blocco non è una condanna: la finestra scade e si riparte da zero.
        Assert.False(_registry.IsBlocked(scope, Now + JoinCodeAttemptPolicy.Window));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void UnIngressoRiuscito_AzzeraIlConteggio()
    {
        var scope = BlockedScope();
        Assert.True(_registry.IsBlocked(scope, Now));

        _registry.RegisterSuccess(scope, Now);

        Assert.False(_registry.IsBlocked(scope, Now));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void IlConteggioDellIndirizzo_BloccaAncheUnTicketNuovo()
    {
        // Un browser che rinnova il ticket a ogni richiesta cambia l'impronta dello scope: il
        // conteggio dell'indirizzo è ciò che gli impedisce di eludere il limite.
        var address = "10.0.0.5";
        for (var attempt = 0; attempt < JoinCodeAttemptPolicy.MaximumFailures; attempt++)
        {
            _registry.RegisterFailure(Scope($"scope-{attempt}", address), Now);
        }

        Assert.True(_registry.IsBlocked(Scope("scope-nuovo", address), Now));
        // Un altro indirizzo con lo stesso numero di tentativi non è coinvolto.
        Assert.False(_registry.IsBlocked(Scope("scope-nuovo", "10.0.0.6"), Now));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void SenzaIndirizzo_ContaSoloLoScope()
    {
        var scope = Scope(address: null);
        for (var attempt = 0; attempt < JoinCodeAttemptPolicy.MaximumFailures; attempt++)
        {
            _registry.RegisterFailure(scope, Now);
        }

        Assert.True(_registry.IsBlocked(scope, Now));
        Assert.False(_registry.IsBlocked(Scope("altro-scope", address: null), Now));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void ConMolteVociScadute_IlConteggioRiparteDopoLaPotatura()
    {
        // Il registro non cresce senza termine: oltre la soglia elimina le finestre scadute. Il
        // caso lo esercita davvero e verifica che il conteggio continui a funzionare.
        for (var index = 0; index < 300; index++)
        {
            _registry.RegisterFailure(Scope($"scope-{index}", address: null), Now);
        }

        var later = Now + JoinCodeAttemptPolicy.Window.Add(TimeSpan.FromSeconds(1));
        var scope = Scope("scope-dopo-la-potatura", address: null);
        for (var attempt = 0; attempt < JoinCodeAttemptPolicy.MaximumFailures; attempt++)
        {
            _registry.RegisterFailure(scope, later);
        }

        Assert.True(_registry.IsBlocked(scope, later));

        // Le finestre scadute non bloccano più nulla, nemmeno interrogate di nuovo.
        Assert.False(_registry.IsBlocked(Scope("scope-0", address: null), later));
        Assert.False(_registry.IsBlocked(Scope("scope-299", address: null), later));
    }

    private JoinCodeAttemptScope BlockedScope()
    {
        var scope = Scope();
        for (var attempt = 0; attempt < JoinCodeAttemptPolicy.MaximumFailures; attempt++)
        {
            _registry.RegisterFailure(scope, Now);
        }

        return scope;
    }

    private static JoinCodeAttemptScope Scope(string admission = "scope-1", string? address = "10.0.0.5") =>
        new(admission, address);
}
