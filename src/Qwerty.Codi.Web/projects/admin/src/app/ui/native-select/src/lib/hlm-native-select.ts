import type { BooleanInput } from '@angular/cdk/coercion';
import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  linkedSignal,
  model,
  output,
  ElementRef,
  viewChild,
} from '@angular/core';
import { type FormValueControl } from '@angular/forms/signals';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronDown } from '@ng-icons/lucide';
import { BrnFieldA11yService, BrnFieldControl, provideBrnLabelable } from '@spartan-ng/brain/field';
import { classes, hlm } from '@spartan-ng/helm/utils';
import type { ClassValue } from 'clsx';

@Component({
  selector: 'hlm-native-select',
  imports: [NgIcon],
  providers: [provideIcons({ lucideChevronDown }), provideBrnLabelable(HlmNativeSelect)],
  changeDetection: ChangeDetectionStrategy.OnPush,
  hostDirectives: [BrnFieldControl],
  host: {
    'data-slot': 'native-select-wrapper',
    '[attr.data-size]': 'size()',
  },
  template: `
    <select
      #select
      data-slot="native-select"
      [id]="selectId()"
      [class]="_computedSelectClass()"
      [attr.data-size]="size()"
      [attr.aria-invalid]="_ariaInvalid() ? 'true' : null"
      [attr.aria-describedby]="_a11y?.describedBy() ?? null"
      [required]="required()"
      [name]="name()"
      [attr.data-invalid]="_ariaInvalid() ? 'true' : null"
      [attr.data-dirty]="_dirty?.() ? 'true' : null"
      [attr.data-touched]="_touched?.() ? 'true' : null"
      [attr.data-matches-spartan-invalid]="_spartanInvalid() ? 'true' : null"
      [value]="value()"
      [disabled]="_disabled()"
      (change)="_valueChanged($event)"
      (blur)="_blur()"
    >
      <ng-content />
    </select>

    <ng-icon
      name="lucideChevronDown"
      [class]="_computedSelectIconClass()"
      aria-hidden="true"
      data-slot="native-select-icon"
    />
  `,
})
export class HlmNativeSelect implements FormValueControl<string> {
  private readonly _fieldControl = inject(BrnFieldControl, { optional: true });
  protected readonly _a11y = inject(BrnFieldA11yService, { optional: true });
  private readonly _select = viewChild<ElementRef<HTMLSelectElement>>('select');
  public readonly required = input(false);
  public readonly name = input('');
  public readonly touch = output<void>();

  private static _id = 0;

  public readonly selectId = input<string>(`hlm-native-select-${HlmNativeSelect._id++}`);

  public readonly selectClass = input<ClassValue>('');

  protected readonly _computedSelectClass = computed(() =>
    hlm(
      'border-input placeholder:text-muted-foreground selection:bg-primary selection:text-primary-foreground dark:bg-input/30 dark:hover:bg-input/50 focus-visible:border-ring focus-visible:ring-ring/50 data-[matches-spartan-invalid=true]:ring-destructive/20 dark:data-[matches-spartan-invalid=true]:ring-destructive/40 data-[matches-spartan-invalid=true]:border-destructive dark:data-[matches-spartan-invalid=true]:border-destructive/50 h-9 w-full min-w-0 appearance-none rounded-md border bg-transparent py-1 ps-2.5 pe-8 text-sm shadow-xs transition-[color,box-shadow] select-none focus-visible:ring-3 data-[matches-spartan-invalid=true]:ring-3 data-[size=sm]:h-8 outline-none disabled:pointer-events-none disabled:cursor-not-allowed',
      this.selectClass(),
    ),
  );

  public readonly selectIconClass = input<ClassValue>('');

  protected readonly _computedSelectIconClass = computed(() =>
    hlm(
      'text-muted-foreground end-2.5 top-1/2 -translate-y-1/2 text-[length:--spacing(4)] pointer-events-none absolute select-none',
      this.selectIconClass(),
    ),
  );

  public readonly size = input<'sm' | 'default'>('default');

  // Signal Forms dichiara `disabled` su `FormValueControl` come trasformazione da `unknown`:
  // con il tipo stretto `BooleanInput` il componente non è assegnabile e la build lo rifiuta.
  public readonly disabled = input<boolean, unknown>(false, { transform: booleanAttribute });

  protected readonly _disabled = linkedSignal(this.disabled);

  /** Whether to force the input into an invalid state. */
  public readonly forceInvalid = input<boolean, BooleanInput>(false, {
    transform: booleanAttribute,
  });

  /** Manual override for aria-invalid. When not set, auto-detects from the parent autocomplete error state. */
  public readonly ariaInvalidInput = input<boolean | undefined, BooleanInput>(undefined, {
    transform: (v: BooleanInput) => (v === '' || v === undefined ? undefined : booleanAttribute(v)),
    alias: 'aria-invalid',
  });

  protected readonly _ariaInvalid = computed(() => this.ariaInvalidInput() ?? this._invalid?.());

  // Adattamento Codi: senza provider CVA, Signal Forms usa il model e il focus custom.
  public readonly value = model<string>('');

  public readonly labelableId = this.selectId;

  protected readonly _invalid = this._fieldControl?.invalid;
  protected readonly _touched = this._fieldControl?.touched;
  protected readonly _dirty = this._fieldControl?.dirty;
  protected readonly _spartanInvalid = computed(
    () => this.forceInvalid() || this._fieldControl?.spartanInvalid(),
  );

  constructor() {
    classes(() => 'group/native-select relative w-fit has-[select:disabled]:opacity-50');
  }

  protected _valueChanged(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.value.set(value);
  }

  protected _blur(): void {
    this.touch.emit();
  }

  public focus(options?: FocusOptions): void {
    this._select()?.nativeElement.focus(options);
  }
}
