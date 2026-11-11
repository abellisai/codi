/**
 * Componenti riusabili condivisi (§20.7, §14.4).
 *
 * Da questo modulo si esportano i componenti standalone riutilizzati da più aree
 * (pulsanti con area interattiva adeguata, stati vuoti, indicatori di freschezza dei
 * dati, dialoghi accessibili), esposti con l'alias di percorso `@shared/*` al posto
 * degli import relativi profondi.
 */
export * from './empty-state/empty-state';
export * from './global-spinner/global-spinner';
export * from './data-table/data-table';
export * from './data-table/data-table-cell';
