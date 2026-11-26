import { Component, effect, input, output, signal } from '@angular/core';
import { Observable } from 'rxjs';
import { sameName } from './names';

/**
 * A text field that suggests values used before (Events, Sites, Tags). The value is committed
 * when the Owner picks a suggestion, presses Enter or leaves the field.
 */
@Component({
  selector: 'app-suggest-input',
  template: `
    <div class="picker" (focusout)="onFocusOut($event)">
      <input
        [id]="inputId()"
        [attr.data-testid]="testId()"
        autocomplete="off"
        role="combobox"
        [attr.aria-expanded]="open() && suggestions().length > 0"
        [placeholder]="placeholder()"
        [value]="text()"
        (input)="onInput($any($event.target).value)"
        (keydown)="onKey($event)"
      />
      @if (open() && suggestions().length > 0) {
        <ul class="picker-list" role="listbox">
          @for (s of suggestions(); track s; let i = $index) {
            <li role="option" [class.active]="i === active()" [attr.aria-selected]="i === active()"
                (mousedown)="$event.preventDefault(); choose(s)">{{ s }}</li>
          }
        </ul>
      }
    </div>
  `,
})
export class SuggestInput {
  readonly inputId = input.required<string>();
  readonly testId = input<string | null>(null);
  readonly placeholder = input('');
  readonly value = input<string | null>(null);
  readonly search = input.required<(term: string) => Observable<string[]>>();
  /** Empty the field after each commit (for adding Tags one by one). */
  readonly clearOnCommit = input(false);
  readonly commit = output<string>();

  protected readonly text = signal('');
  protected readonly open = signal(false);
  protected readonly active = signal(-1);
  protected readonly suggestions = signal<string[]>([]);
  private timer: ReturnType<typeof setTimeout> | undefined;
  private committed = '';

  constructor() {
    effect(() => {
      this.committed = this.value() ?? '';
      this.text.set(this.committed);
    });
  }

  protected onInput(value: string) {
    this.text.set(value);
    this.open.set(true);
    this.active.set(-1);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => {
      const term = value.trim();
      if (!term) return this.suggestions.set([]);
      this.search()(term).subscribe((list) =>
        this.suggestions.set(list.filter((s) => !sameName(s, term)).slice(0, 8)),
      );
    }, 150);
  }

  protected onKey(event: KeyboardEvent) {
    const count = this.suggestions().length;
    if (event.key === 'ArrowDown' && count) {
      this.active.update((i) => (i + 1) % count);
      event.preventDefault();
    } else if (event.key === 'ArrowUp' && count) {
      this.active.update((i) => (i <= 0 ? count - 1 : i - 1));
      event.preventDefault();
    } else if (event.key === 'Enter') {
      const pick = this.open() && this.active() >= 0 ? this.suggestions()[this.active()] : this.text();
      this.choose(pick);
      event.preventDefault();
    } else if (event.key === 'Escape') {
      this.open.set(false);
    }
  }

  protected onFocusOut(event: FocusEvent) {
    const next = event.relatedTarget as Node | null;
    if (!next || !(event.currentTarget as HTMLElement).contains(next)) this.choose(this.text());
  }

  protected choose(value: string) {
    this.open.set(false);
    this.suggestions.set([]);
    const trimmed = value.trim();
    if (this.clearOnCommit()) {
      this.text.set('');
      if (trimmed) this.commit.emit(trimmed);
      return;
    }
    this.text.set(trimmed);
    if (trimmed !== this.committed) {
      this.committed = trimmed;
      this.commit.emit(trimmed);
    }
  }
}
