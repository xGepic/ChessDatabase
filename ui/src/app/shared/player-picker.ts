import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { Api, Player, problemMessage } from '../api/api';
import { sameName } from './names';

/**
 * Opponent autocomplete: suggests existing Players as you type and offers to create a new
 * Player inline when the name is new, so the same person is never recorded twice.
 */
@Component({
  selector: 'app-player-picker',
  template: `
    <div class="picker" (focusout)="onFocusOut($event)">
      <input
        [id]="inputId()"
        data-testid="opponent-input"
        autocomplete="off"
        role="combobox"
        [attr.aria-expanded]="open()"
        [value]="text()"
        (input)="onInput($any($event.target).value)"
        (focus)="open.set(true)"
        (keydown)="onKey($event)"
        placeholder="Type a name"
      />
      @if (open() && options().length > 0) {
        <ul class="picker-list" role="listbox">
          @for (option of options(); track option.key; let i = $index) {
            <li
              role="option"
              [class.active]="i === active()"
              [attr.aria-selected]="i === active()"
              [attr.data-testid]="option.player ? 'opponent-option' : 'opponent-create'"
              (mousedown)="$event.preventDefault(); choose(option)"
            >
              @if (option.player) {
                {{ option.player.name }}
              } @else {
                Create new Player <strong>{{ option.label }}</strong>
              }
            </li>
          }
        </ul>
      }
      @if (error()) {
        <p class="field-error">{{ error() }}</p>
      }
    </div>
  `,
})
export class PlayerPicker {
  private readonly api = inject(Api);

  readonly inputId = input('opponent');
  readonly player = input<Player | null>(null);
  readonly playerChange = output<Player>();

  protected readonly text = signal('');
  protected readonly open = signal(false);
  protected readonly active = signal(0);
  protected readonly error = signal('');
  private readonly suggestions = signal<Player[]>([]);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly options = computed(() => {
    const typed = this.text().trim();
    const list: { key: string; label: string; player: Player | null }[] = this.suggestions().map((p) => ({
      key: `p${p.id}`,
      label: p.name,
      player: p,
    }));
    const exact = this.suggestions().some((p) => sameName(p.name, typed));
    if (typed && !exact) list.push({ key: 'new', label: typed, player: null });
    return list;
  });

  constructor() {
    effect(() => this.text.set(this.player()?.name ?? ''));
  }

  protected onInput(value: string) {
    this.text.set(value);
    this.open.set(true);
    this.active.set(0);
    this.error.set('');
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      const term = value.trim();
      if (!term) return this.suggestions.set([]);
      this.api.searchPlayers(term).subscribe((players) => this.suggestions.set(players.slice(0, 8)));
    }, 150);
  }

  protected onKey(event: KeyboardEvent) {
    const count = this.options().length;
    if (event.key === 'ArrowDown' && count) {
      this.active.update((i) => (i + 1) % count);
      event.preventDefault();
    } else if (event.key === 'ArrowUp' && count) {
      this.active.update((i) => (i - 1 + count) % count);
      event.preventDefault();
    } else if (event.key === 'Enter' && this.open() && count) {
      this.choose(this.options()[this.active()]);
      event.preventDefault();
    } else if (event.key === 'Escape') {
      this.open.set(false);
    }
  }

  protected onFocusOut(event: FocusEvent) {
    const next = event.relatedTarget as Node | null;
    if (!next || !(event.currentTarget as HTMLElement).contains(next)) {
      this.open.set(false);
      this.text.set(this.player()?.name ?? this.text());
    }
  }

  protected choose(option: { label: string; player: Player | null }) {
    this.open.set(false);
    if (option.player) {
      this.text.set(option.player.name);
      this.playerChange.emit(option.player);
      return;
    }
    this.api.createPlayer(option.label).subscribe({
      next: (player) => {
        this.text.set(player.name);
        this.playerChange.emit(player);
      },
      error: (e) => this.error.set(problemMessage(e)),
    });
  }
}
