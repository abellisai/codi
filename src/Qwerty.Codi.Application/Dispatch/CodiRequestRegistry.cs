using System.Reflection;
using System.Text.RegularExpressions;
using Mediator;

namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Registro delle richieste di §4.4. Scopre i record all'avvio, ne espone nome, natura e policy,
/// rifiuta il catalogo se una richiesta non dichiara una policy o se una richiesta anonima non
/// appartiene all'elenco chiuso di §10.4, e risolve il nome pubblico in un descrittore eseguibile.
/// </summary>
public sealed class CodiRequestRegistry
{
    private static readonly Regex PublicNamePattern = new(
        @"^[A-Z][A-Za-z0-9]*$",
        RegexOptions.CultureInvariant);

    private readonly IReadOnlyDictionary<(CodiRequestKind Kind, string Name), CodiRequestDescriptor> _byChannel;

    private CodiRequestRegistry(IReadOnlyList<CodiRequestDescriptor> descriptors)
    {
        Descriptors = descriptors;
        _byChannel = descriptors.ToDictionary(descriptor => (descriptor.Kind, descriptor.Name));
        MissingAnonymousBootstrapRequests = [.. AnonymousBootstrapRequests.All
            .Where(name => !descriptors.Any(descriptor => string.Equals(descriptor.Name, name, StringComparison.Ordinal)))];
    }

    /// <summary>Richieste note, in ordine di scoperta.</summary>
    public IReadOnlyList<CodiRequestDescriptor> Descriptors { get; }

    /// <summary>
    /// Richieste anonime di §10.4 ancora non implementate. Non è un errore — gli incrementi le
    /// aggiungono una alla volta — ma è un dato da registrare all'avvio, perché un elenco
    /// incompleto ha lo stesso effetto pratico di un elenco sbagliato.
    /// </summary>
    public IReadOnlyList<string> MissingAnonymousBootstrapRequests { get; }

    /// <summary>
    /// Costruisce il registro dalle richieste pubbliche degli assembly indicati: è il percorso
    /// di avvio, quindi scarta i tipi non pubblici e ignora ciò che non è una richiesta.
    /// </summary>
    public static CodiRequestRegistry FromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return FromTypes(assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false, IsPublic: true }));
    }

    /// <summary>
    /// Costruisce il registro da un insieme esplicito di tipi. È il punto di ingresso dei test,
    /// che devono poter comporre cataloghi volutamente incoerenti senza inquinare l'assembly.
    /// </summary>
    public static CodiRequestRegistry FromTypes(IEnumerable<Type> requestTypes)
    {
        ArgumentNullException.ThrowIfNull(requestTypes);

        var descriptors = new List<CodiRequestDescriptor>();

        foreach (var type in requestTypes)
        {
            if (type is not { IsClass: true, IsAbstract: false })
            {
                continue;
            }

            var responseType = FindResponseType(type);
            if (responseType is null)
            {
                // Non è una richiesta del dispatcher: il tipo esiste per altro.
                continue;
            }

            descriptors.Add(CreateDescriptor(type, responseType));
        }

        return new CodiRequestRegistry(EnsureUniqueNames(descriptors));
    }

    /// <summary>
    /// Costruisce il registro da descrittori già composti: è il punto di ingresso di chi esegue
    /// un catalogo esplicito, come i test del percorso di esecuzione, che non possono ospitare il
    /// generatore di Mediator e quindi non possono comporre un mediator per le proprie voci.
    /// </summary>
    public static CodiRequestRegistry FromDescriptors(IEnumerable<CodiRequestDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        return new CodiRequestRegistry(EnsureUniqueNames([.. descriptors]));
    }

    /// <summary>
    /// Cerca la richiesta nel canale giusto. Una query cercata fra i comandi non si trova: i due
    /// canali di §10.1 hanno registri distinti, anche se un nome esiste nell'altro.
    /// </summary>
    public CodiRequestDescriptor? Find(CodiRequestKind kind, string requestName) =>
        _byChannel.GetValueOrDefault((kind, requestName));

    private static CodiRequestDescriptor CreateDescriptor(Type type, Type responseType)
    {
        var describesCommand = ImplementsOpenInterface(type, typeof(ICodiCommand<>));
        var describesQuery = ImplementsOpenInterface(type, typeof(ICodiQuery<>));

        if (describesCommand == describesQuery)
        {
            // Una richiesta senza policy o con due nature è un errore di catalogo, non una
            // richiesta da scartare: il registro esiste proprio per accorgersene all'avvio.
            throw new CodiDispatchConfigurationException(
                $"La richiesta '{type.FullName}' deve dichiarare esattamente una natura fra ICodiCommand<T> e ICodiQuery<T>, quindi una policy (§9.3).");
        }

        var name = ReadStaticMember(type, nameof(ICodiRequest.RequestName)) as string;
        if (string.IsNullOrWhiteSpace(name) || !PublicNamePattern.IsMatch(name))
        {
            throw new CodiDispatchConfigurationException(
                $"La richiesta '{type.FullName}' dichiara il nome pubblico '{name}': è richiesto un nome PascalCase, senza spazi né punti (§10.1).");
        }

        var policyMember = ReadStaticMember(type, nameof(ICodiRequest.Policy));
        if (policyMember is not CodiPolicy policy)
        {
            throw new CodiDispatchConfigurationException(
                $"La richiesta '{name}' non dichiara una policy di §9.3.");
        }

        var kind = describesCommand ? CodiRequestKind.Command : CodiRequestKind.Query;
        var isLocalOnly = typeof(ILocalOnlyRequest).IsAssignableFrom(type);

        EnsureAnonymousAccessIsDeclared(name, policy, isLocalOnly);

        return new CodiRequestDescriptor(
            name,
            kind,
            policy,
            type,
            responseType,
            isLocalOnly,
            CreateInvoker(type, responseType));
    }

    /// <summary>
    /// Verifica le regole dell'accesso anonimo: nessuna richiesta anonima fuori dall'elenco
    /// chiuso, i nomi dell'elenco chiuso effettivamente anonimi, l'unica richiesta anonima locale
    /// è il bootstrap amministrativo, e il bootstrap amministrativo è anonimo e locale (§9.3, §10.4).
    /// </summary>
    private static void EnsureAnonymousAccessIsDeclared(string name, CodiPolicy policy, bool isLocalOnly)
    {
        var isAnonymous = policy == CodiPolicy.AnonymousBootstrap;
        var isListed = AnonymousBootstrapRequests.All.Contains(name);

        if (isAnonymous && !isListed)
        {
            throw new CodiDispatchConfigurationException(
                $"La richiesta '{name}' chiede AnonymousBootstrap ma non è fra quelle previste da §10.4: l'accesso anonimo non si estende per omissione.");
        }

        if (isListed && !isAnonymous)
        {
            // Anche il caso opposto rompe il contratto: un 'JoinSession' che richiede Admin
            // renderebbe impossibile l'ingresso descritto da §9.4.
            throw new CodiDispatchConfigurationException(
                $"La richiesta '{name}' è una di quelle anonime di §10.4 e deve dichiarare AnonymousBootstrap, non {policy}.");
        }

        if (isAnonymous && isLocalOnly && !string.Equals(name, AnonymousBootstrapRequests.BootstrapAdminAccount, StringComparison.Ordinal))
        {
            throw new CodiDispatchConfigurationException(
                $"La richiesta anonima '{name}' è dichiarata locale: fra le richieste anonime solo {AnonymousBootstrapRequests.BootstrapAdminAccount} è fuori dal dispatcher HTTP.");
        }

        if (string.Equals(name, AnonymousBootstrapRequests.BootstrapAdminAccount, StringComparison.Ordinal) &&
            (!isAnonymous || !isLocalOnly))
        {
            throw new CodiDispatchConfigurationException(
                $"{AnonymousBootstrapRequests.BootstrapAdminAccount} è anonima e locale per definizione: non è esposta via HTTP e non richiede un contesto (§10.4).");
        }
    }

    /// <summary>
    /// Verifica che lo stesso nome pubblico non identifichi due richieste dello stesso canale: il
    /// nome è la chiave di risoluzione, quindi una ripetizione renderebbe l'esito dipendente
    /// dall'ordine di scoperta.
    /// </summary>
    private static IReadOnlyList<CodiRequestDescriptor> EnsureUniqueNames(
        IReadOnlyList<CodiRequestDescriptor> descriptors)
    {
        var duplicates = descriptors
            .GroupBy(descriptor => (descriptor.Kind, descriptor.Name))
            .Where(group => group.Count() > 1)
            .Select(group => $"'{group.Key.Name}' ({group.Key.Kind})")
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new CodiDispatchConfigurationException(
                $"Nomi pubblici ripetuti nel registro: {string.Join(", ", duplicates)}.");
        }

        return descriptors;
    }

    private static bool ImplementsOpenInterface(Type type, Type openInterface) =>
        type.GetInterfaces().Any(implemented =>
            implemented.IsGenericType && implemented.GetGenericTypeDefinition() == openInterface);

    private static Type? FindResponseType(Type type) =>
        type.GetInterfaces()
            .FirstOrDefault(implemented =>
                implemented.IsGenericType && implemented.GetGenericTypeDefinition() == typeof(IRequest<>))
            ?.GetGenericArguments()[0];

    /// <summary>
    /// Legge un metadato dichiarato come membro statico della richiesta, implementando il membro
    /// astratto statico di <see cref="ICodiRequest"/>. È pubblico e statico, quindi il registro lo
    /// conosce all'avvio senza costruire un'istanza.
    /// </summary>
    private static object? ReadStaticMember(Type type, string memberName) =>
        type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

    private static ICodiRequestInvoker CreateInvoker(Type requestType, Type responseType)
    {
        var invokerType = typeof(MediatorRequestInvoker<,>).MakeGenericType(requestType, responseType);

        return Activator.CreateInstance(invokerType, nonPublic: true) as ICodiRequestInvoker
            ?? throw new CodiDispatchConfigurationException(
                $"Non è stato possibile costruire l'invocazione della richiesta '{requestType.FullName}'.");
    }
}
