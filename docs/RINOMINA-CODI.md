# Rinomina locale in Codì

Data: 16 ottobre 2026.

Il prodotto si chiama **Codì**. I nomi tecnici usano `Codi` senza accento.
La rinomina riguarda il progetto locale. Il repository GitHub conserva il proprio indirizzo.

## Nomi correnti

| Elemento | Nome |
|---|---|
| Cartella principale | `Codi` |
| Soluzione | `Qwerty.Codi.slnx` |
| Progetti e namespace .NET | `Qwerty.Codi.*` |
| Workspace npm | `codi-web-workspace` |
| Applicazioni npm | `codi-admin`, `codi-public` |
| Alias della libreria Angular | `@codi/api` |
| Sezione di configurazione | `Codi` |
| Variabile del primo accesso | `CODI_BOOTSTRAP_PASSWORD` |
| Variabili di configurazione | `Codi__DataDirectory`, `Codi__Realtime__AllowedOrigins__0` |
| Cookie | Prefisso `__Host-Codi` |
| Header antiforgery | `X-Codi-Antiforgery` |
| Database locale | `src/Qwerty.Codi.Api/data/codi.db` |
| Skill locali | `codi-spartan`, `codi-signal-forms` |

I quattro launcher Windows mantengono i propri nomi.
Le porte restano 5200 per il server, 4200 per il docente e 4300 per gli alunni.

## Compatibilità dei dati

La rinomina non modifica il contenuto del database locale o della sua copia preesistente.
I due file cambiano nome e mantengono le stesse impronte SHA-256.
Le 12 migrazioni SQL mantengono contenuto e checksum.

I tipi dei blocchi `aula_*` appartengono al formato dei progetti già salvati.
Il nome IndexedDB `aulaviva-attempt-recovery` identifica le copie locali già presenti nei browser.
Entrambi restano invariati per mantenere accessibili i lavori esistenti.
Anche le versioni dei motori, le impronte dei progetti e i formati dei checkpoint restano invariati.

I nuovi cookie richiedono un nuovo accesso del docente e un nuovo ingresso degli alunni.
Le configurazioni esterne devono usare la sezione `Codi` e le nuove variabili.
Gli archivi dei collaudi precedenti conservano i risultati storici.

## Verifiche

Gli esiti della soluzione rinominata sono nel [registro di sviluppo](../.github/STATO-SVILUPPO.md).
