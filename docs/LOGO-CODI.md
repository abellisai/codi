# Logo Codì

Il logo riprende la proposta approvata il 16 ottobre 2026.
Tre blocchi formano la C. La scritta è blu scuro e l'accento è giallo.

## File vettoriali

I file condivisi sono nella cartella `src/Qwerty.Codi.Web/public/brand`.

| File | Uso |
|---|---|
| [codi-logo.svg](../src/Qwerty.Codi.Web/public/brand/codi-logo.svg) | Logo completo con scritta Codì |
| [codi-mark.svg](../src/Qwerty.Codi.Web/public/brand/codi-mark.svg) | Simbolo per il menu compresso e la scheda del browser |

Entrambi i file contengono solo forme vettoriali.
Anche la scritta usa tracciati, senza font esterni o immagini incorporate.
Gli sfondi sono trasparenti. Le dimensioni dichiarate riservano lo spazio prima del caricamento.

## Colori

| Elemento | Colore |
|---|---|
| Scritta | `#17334b` |
| Blocco blu | `#079df1` |
| Blocco verde | `#17c99d` |
| Blocco giallo e accento | `#ffbd00` |

## Applicazione

L'app pubblica mostra il logo nell'intestazione comune a tutte le pagine.
L'area docente mostra il logo all'accesso e nel menu laterale.
Il menu compresso usa soltanto il simbolo, con lo stesso nome accessibile del collegamento.
Il tema scuro dell'admin mostra il logo completo in bianco.
Entrambe le app usano il simbolo SVG come icona della scheda del browser.

I percorsi degli asset sono relativi alla base delle rispettive app.
Il pacchetto statico contiene `brand` per l'app pubblica e `admin/brand` per l'area docente.
