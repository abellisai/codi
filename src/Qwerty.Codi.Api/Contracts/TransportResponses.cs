namespace Qwerty.Codi.Api.Contracts;

/// <summary>
/// Corpi delle **risposte dedicate** dell'area HTTP (§10.3): l'accesso amministrativo, l'ingresso
/// del partecipante e la coppia antiforgery. Sono trasporto, non contratto applicativo: ognuno è una
/// **proiezione** di un DTO più ricco di <c>Application</c>, tolta dei campi che non devono uscire —
/// la marca di sicurezza, l'identificativo della credenziale, il ticket di ammissione.
///
/// Stavano come oggetti anonimi costruiti dentro gli endpoint: la forma che il client legge esisteva
/// solo nel codice che la scriveva. Qui sono tipi con un nome, quindi si possono cercare, elencare e
/// confrontare con il client; i DTO del dispatcher stanno invece in `Application/Dto`, dove la 0.42 li
/// ha raccolti tutti, uno per file. Nessuno di questi record espone un campo in più rispetto a prima:
/// il contratto sul filo è invariato.
/// </summary>
internal static class TransportResponses;

/// <summary>
/// Profilo minimo dell'amministratore per <c>GET /api/v1/admin/auth/me</c>: chi è e che cosa può
/// fare. La marca di sicurezza resta nel cookie protetto e non compare qui (§9.2).
/// </summary>
internal sealed record AdminProfileResponse(string Username, IReadOnlyList<string> Policies);

/// <summary>
/// Esito dell'accesso per <c>POST /api/v1/admin/auth/login</c>: il solo nome. Il resto lo legge il
/// client dal profilo, che rilegge la credenziale a ogni richiesta.
/// </summary>
internal sealed record AdminSignInResponse(string Username);

/// <summary>
/// Esito dell'ingresso per <c>POST /api/v1/public/join</c>: chi è entrato e che cosa deve mostrare.
/// Non contiene la credenziale, il ticket né l'impronta della postazione: quelli restano nel cookie
/// e nel server.
/// </summary>
internal sealed record ParticipantJoinResponse(Guid ParticipantId, Guid SessionId, string DisplayName, string View);

/// <summary>
/// Coppia antiforgery per <c>GET /api/v1/{area}/bootstrap</c>: i nomi del cookie e dell'intestazione
/// che il client deve usare. Il valore del token viaggia nel cookie, non nel corpo.
/// </summary>
internal sealed record AntiforgeryBootstrapResponse(string AntiforgeryCookieName, string AntiforgeryHeaderName);
