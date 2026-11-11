import eslint from '@eslint/js';
import tseslint from 'typescript-eslint';
import angular from 'angular-eslint';
import eslintConfigPrettier from 'eslint-config-prettier';

/**
 * Configurazione ESLint (flat config) del frontend Codi (§20.7).
 *
 * Applica le regole Angular e TypeScript ai sorgenti e ai template HTML; in coda
 * `eslint-config-prettier` disattiva le regole in conflitto con Prettier, così
 * `npm run lint` e `npm run format:check` restano giudizi distinti e non si
 * sovrappongono sulla sola formattazione.
 *
 * `ng lint` non esiste più come builder predefinito in Angular: gli script di
 * `package.json` invocano ESLint direttamente.
 *
 * Il file è in forma ESM (`eslint.config.mjs`) e non CommonJS: importare un pacchetto
 * funziona sia che esponga ESM sia che esponga CommonJS, mentre richiederlo fallisce se
 * espone solo ESM. Le interfacce di questi plugin cambiano fra versioni maggiori, e il
 * modulo ESM è la forma che entrambe documentano.
 */
export default tseslint.config(
  {
    // Build, cache e dipendenze non sono sorgenti da verificare.
    ignores: ['dist/**', '.angular/**', 'coverage/**', 'node_modules/**'],
  },
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      ...tseslint.configs.recommended,
      ...tseslint.configs.stylistic,
      ...angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'app', style: 'camelCase' },
      ],
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: 'app', style: 'kebab-case' },
      ],
    },
  },
  {
    files: ['**/*.html'],
    extends: [...angular.configs.templateRecommended, ...angular.configs.templateAccessibility],
    rules: {},
  },
  {
    // Helm è codice del generatore Spartan mantenuto localmente: conserva i suoi selettori —
    // `hlm*` sugli attributi e `hlm-*` sugli elementi — e i suoi alias di input, che fanno parte
    // dell'API con cui i componenti si compongono (`class` è il caso ricorrente).
    files: ['projects/admin/src/app/ui/**/*.ts'],
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        { type: 'attribute', prefix: 'hlm', style: 'camelCase' },
      ],
      '@angular-eslint/component-selector': [
        'error',
        { type: 'element', prefix: 'hlm', style: 'kebab-case' },
      ],
      '@angular-eslint/no-input-rename': 'off',
    },
  },
  {
    files: ['projects/public/**/*.ts', 'libraries/**/*.ts'],
    rules: {
      // Le librerie comuni e la UI alunni non importano l'admin né il suo design system.
      'no-restricted-imports': [
        'error',
        {
          patterns: ['@core/*', '@shared/*', '@spartan-ng/*', '**/admin/**'],
        },
      ],
    },
  },
  eslintConfigPrettier,
);
