namespace Qwerty.Codi.Infrastructure.Persistence;

/// <summary>
/// Esito della preparazione del database (§16.1): modo di journal effettivo e versione di schema
/// applicata. Il chiamante li registra e li confronta, invece di darli per scontati.
/// </summary>
public sealed record DatabasePreparation(string JournalMode, int SchemaVersion);
