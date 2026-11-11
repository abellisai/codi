import { describeAnswerRecord, describeWork } from './preview-lines';

/**
 * Lettura del lavoro dell'alunno per il docente (§15.5). Il lavoro ha due forme — un progetto a
 * blocchi e un verbale di risposta — e la versione del motore fissata dall'assegnazione dice quale:
 * leggere l'uno con le regole dell'altro mostrerebbe una card vuota senza spiegare perché.
 */
describe('Lettura del lavoro dell’alunno', () => {
  const program = JSON.stringify({
    blocks: {
      languageVersion: 0,
      blocks: [
        { type: 'aula_start', id: 'start', next: { block: { type: 'aula_move', id: 'm1' } } },
      ],
    },
  });

  it('un progetto a blocchi si legge come elenco di blocchi', () => {
    const lines = describeWork(program, 'gridpath-1');

    expect(lines.map((line) => line.label)).toEqual(['quando si avvia', 'fai un passo']);
  });

  it('un verbale di risposta si legge come la risposta scritta', () => {
    const lines = describeWork('{"answer":"6"}', 'quiz-1');

    expect(lines).toEqual([{ depth: 0, label: 'Risposta: 6' }]);
  });

  it('un verbale senza risposta lo dice, invece di sembrare un lavoro mai arrivato', () => {
    // «Non ha ancora risposto» è un'informazione: il docente deve poterla distinguere da un lavoro
    // che non è arrivato affatto.
    expect(describeWork('{"answer":""}', 'quiz-1')).toEqual([
      { depth: 0, label: 'Nessuna risposta scritta' },
    ]);
  });

  it('la versione del motore decide la lettura, non il contenuto', () => {
    // Lo stesso testo letto con il motore sbagliato non produce righe: il verbale non ha blocchi e
    // il progetto non ha una risposta.
    expect(describeWork('{"answer":"6"}', 'gridpath-1')).toEqual([]);
    expect(describeWork(program, 'quiz-1')).toEqual([
      { depth: 0, label: 'Nessuna risposta scritta' },
    ]);
  });

  it('un lavoro assente non produce righe', () => {
    expect(describeWork(null, 'gridpath-1')).toEqual([]);
    expect(describeWork(null, 'quiz-1')).toEqual([]);
  });

  it('un verbale illeggibile non inventa una risposta', () => {
    expect(describeAnswerRecord('{ non è JSON }')).toEqual([]);
  });
});
