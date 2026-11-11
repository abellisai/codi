import { PathKind, required, SchemaPath, SchemaPathRules, validate } from '@angular/forms/signals';

type TextPath = SchemaPath<string, SchemaPathRules.Supported, PathKind.Child>;

/** Validazione di cortesia: unicità, autorizzazione e versione restano responsabilità del server. */
export function textRules(path: TextPath, label: string, limit: number, mandatory = true): void {
  if (mandatory) {
    required(path, { message: `${label}: compila il campo.` });
  }
  validate(path, ({ value }) => {
    const text = value().trim();
    if (mandatory && value() !== '' && text === '') {
      return { kind: 'blank', message: `${label}: inserisci del testo, non solo spazi.` };
    }
    return text.length > limit
      ? { kind: 'length', message: `${label}: usa al massimo ${limit} caratteri.` }
      : undefined;
  });
}

export function schoolYearRules(path: TextPath): void {
  required(path, { message: 'Inserisci l’anno scolastico.' });
  validate(path, ({ value }) => {
    const year = value().trim();
    if (year === '') return undefined;
    const match = /^([1-9]\d{3})[/-]([1-9]\d{3})$/.exec(year);
    return match !== null && Number(match[2]) === Number(match[1]) + 1
      ? undefined
      : { kind: 'schoolYear', message: 'Usa due anni consecutivi, per esempio 2026/2027.' };
  });
}
