namespace Qwerty.Codi.Application.Dispatch;

/// <summary>
/// Natura di una richiesta: mutazione o lettura. Il catalogo di §10.4–§10.5 e i due endpoint di
/// §10.1 sono separati per natura, quindi il nome pubblico va cercato nel registro giusto.
/// </summary>
public enum CodiRequestKind
{
    /// <summary>Mutazione, esposta da <c>POST /api/v1/{area}/execute/command/{commandName}</c>.</summary>
    Command,

    /// <summary>Lettura senza mutazioni persistenti, esposta dalla rotta delle query.</summary>
    Query,
}
