import { Component, computed, inject, input, output, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { ANALYSIS_SECONDS, Api, Engine, EngineForm, EngineSelection, UciOption, problemMessage } from '../api/api';

/** A new definition for Lc0 (Leela Chess Zero): few CPU threads, the network and the GPU backend as options. */
const LC0_TEMPLATE: EngineForm = {
  name: 'Lc0',
  path: '',
  threads: 2,
  hashMb: 64,
  multiPv: 3,
  interactiveSecondsLimit: null,
  analysisSecondsPerMove: 2,
  options: [
    { name: 'WeightsFile', value: '' },
    { name: 'Backend', value: 'cuda-auto' },
  ],
};

const OTHER_TEMPLATE: EngineForm = { ...LC0_TEMPLATE, name: '', threads: 4, hashMb: 1024, options: [] };

/** One Engine definition's form. Saving checks the program by starting it once, so it happens on the button. */
@Component({
  selector: 'app-engine-form',
  template: `
    <form class="form-grid engine-form" [attr.data-testid]="'engine-form-' + (engine()?.name ?? 'new')"
          (submit)="$event.preventDefault(); save()">
      <label [for]="id('name')">Name</label>
      <input [id]="id('name')" data-testid="engine-label" [value]="draft().name" (input)="set('name', value($event))"
             placeholder="e.g. Lc0" />

      <label [for]="id('path')">Program</label>
      <div>
        <input [id]="id('path')" data-testid="engine-path" [value]="draft().path" (input)="set('path', value($event))"
               placeholder="e.g. C:\\Tools\\stockfish\\stockfish.exe" />
        <p class="hint">
          @if (engine()?.reportedName; as reported) { Found <strong data-testid="engine-name">{{ reported }}</strong>. }
          The engine's .exe. Replace it any time to update the engine.
        </p>
      </div>

      <label [for]="id('threads')">Threads</label>
      <div>
        <input [id]="id('threads')" class="elo" type="number" min="1" max="1024" [value]="draft().threads"
               (input)="set('threads', number($event))" />
        <p class="hint">CPU threads. Lc0 mostly uses the GPU and needs only a few.</p>
      </div>

      <label [for]="id('hash')">Hash (MB)</label>
      <div>
        <input [id]="id('hash')" class="elo" type="number" min="1" [value]="draft().hashMb" (input)="set('hashMb', number($event))" />
        <p class="hint">Engines without a Hash option, such as Lc0, skip it.</p>
      </div>

      <label [for]="id('lines')">Lines</label>
      <div>
        <input [id]="id('lines')" class="elo" type="number" min="1" max="3" [value]="draft().multiPv" (input)="set('multiPv', number($event))" />
        <p class="hint">How many best lines Interactive Analysis shows (up to 3).</p>
      </div>

      <label [for]="id('limit')">Time limit</label>
      <div>
        <input [id]="id('limit')" class="elo" type="number" min="1" max="3600" [value]="draft().interactiveSecondsLimit ?? ''"
               placeholder="none" (input)="set('interactiveSecondsLimit', optionalNumber($event))" /> seconds
        <p class="hint">How long Interactive Analysis thinks about a position. Leave empty to let it think until you move on.</p>
      </div>

      <label [for]="id('seconds')">Game Analysis</label>
      <div>
        <input [id]="id('seconds')" class="elo" type="number" [min]="analysisSeconds.min" [max]="analysisSeconds.max" step="0.1"
               [value]="draft().analysisSecondsPerMove" (input)="set('analysisSecondsPerMove', number($event))" /> seconds per position
        <p class="hint">Game Analysis uses one line and the threads and hash above.</p>
      </div>

      <span class="form-label">Options</span>
      <div>
        @for (option of draft().options; track $index; let i = $index) {
          <div class="option-row" data-testid="engine-option">
            <input aria-label="Option name" class="option-name" [value]="option.name" (input)="setOption(i, 'name', value($event))"
                   placeholder="name" />
            <input aria-label="Option value" class="option-value" [value]="option.value" (input)="setOption(i, 'value', value($event))"
                   placeholder="value" />
            <button type="button" class="link-button" (click)="removeOption(i)" [attr.aria-label]="'Remove option ' + option.name">Remove</button>
          </div>
        }
        <button type="button" class="link-button" data-testid="engine-add-option" (click)="addOption()">+ Option</button>
        <p class="hint">Further UCI options, sent as the engine starts. For Lc0: WeightsFile is the network file (.pb.gz)
          and Backend picks the GPU code (cuda-auto for an NVIDIA card). Options the engine doesn't know are skipped.</p>
      </div>

      <span></span>
      <div class="form-actions">
        <button type="submit" class="primary" data-testid="engine-save" [disabled]="saving()">{{ engine() ? 'Save' : 'Add' }}</button>
        @if (removable()) {
          <button type="button" data-testid="engine-remove" (click)="remove()">{{ engine() ? 'Remove' : 'Cancel' }}</button>
        }
        @if (message()) { <span class="status" data-testid="engine-status" role="status">{{ message() }}</span> }
      </div>
    </form>
  `,
})
export class EngineFormComponent {
  private readonly api = inject(Api);

  /** The saved definition; null for a new one, started from the template. */
  readonly engine = input<Engine | null>(null);
  readonly template = input<EngineForm>(OTHER_TEMPLATE);
  /** Whether the Remove (or, for a new one, Cancel) button shows: the last Engine can't go. */
  readonly removable = input(true);
  readonly saved = output<Engine>();
  readonly removed = output<void>();

  protected readonly analysisSeconds = ANALYSIS_SECONDS;
  protected readonly saving = signal(false);
  protected readonly message = signal('');
  private readonly edits = signal<Partial<EngineForm>>({});
  protected readonly draft = computed<EngineForm>(() => ({ ...(this.engine() ?? this.template()), ...this.edits() }));

  private static nextId = 0;
  private readonly formId = `engine-${EngineFormComponent.nextId++}`;
  protected id(field: string) {
    return `${this.formId}-${field}`;
  }

  protected value(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }
  protected number(event: Event): number {
    return Number(this.value(event));
  }
  protected optionalNumber(event: Event): number | null {
    const text = this.value(event).trim();
    return text ? Number(text) : null;
  }

  protected set<K extends keyof EngineForm>(field: K, value: EngineForm[K]) {
    this.edits.update((e) => ({ ...e, [field]: value }));
    this.message.set('');
  }

  protected setOption(index: number, field: keyof UciOption, value: string) {
    this.set('options', this.draft().options.map((o, i) => (i === index ? { ...o, [field]: value } : o)));
  }
  protected addOption() {
    this.set('options', [...this.draft().options, { name: '', value: '' }]);
  }
  protected removeOption(index: number) {
    this.set('options', this.draft().options.filter((_, i) => i !== index));
  }

  protected save() {
    const engine = this.engine();
    this.saving.set(true);
    this.message.set('');
    (engine ? this.api.saveEngine(engine.id, this.draft()) : this.api.addEngine(this.draft())).subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.edits.set({});
        this.message.set(saved.reportedName ? `Saved. Found ${saved.reportedName}.` : 'Saved');
        this.saved.emit(saved);
      },
      error: (e) => {
        this.saving.set(false);
        this.message.set(problemMessage(e));
      },
    });
  }

  protected remove() {
    const engine = this.engine();
    if (!engine) {
      this.removed.emit();
      return;
    }
    if (!confirm(`Remove ${engine.name}? Whatever used it switches to the first remaining engine.`)) return;
    this.api.removeEngine(engine.id).subscribe({
      next: () => this.removed.emit(),
      error: (e) => this.message.set(problemMessage(e)),
    });
  }
}

/** Settings → Engines: every Engine definition, and which one Interactive Analysis and Game Analysis use. */
@Component({
  selector: 'app-engine-settings',
  imports: [EngineFormComponent],
  template: `
    <section class="card narrow-card" data-testid="engine-settings">
      <header class="card-head"><h2>Engines</h2></header>
      @if (selection(); as s) {
        <div class="form-grid">
          <label for="interactive-engine">Interactive Analysis</label>
          <div>
            <select id="interactive-engine" data-testid="interactive-engine" (change)="select('interactiveEngineId', $event)">
              @for (e of engines(); track e.id) { <option [value]="e.id" [selected]="e.id === s.interactiveEngineId">{{ e.name }}</option> }
            </select>
            <p class="hint">A change takes effect on the next move in an open Game.</p>
          </div>

          <label for="analysis-engine">Game Analysis</label>
          <div>
            <select id="analysis-engine" data-testid="analysis-engine" (change)="select('analysisEngineId', $event)">
              @for (e of engines(); track e.id) { <option [value]="e.id" [selected]="e.id === s.analysisEngineId">{{ e.name }}</option> }
            </select>
            <p class="hint">Mistake Training judges your answers with the engine that analysed the Game.
              @if (selectionMessage()) { <span class="status" role="status" data-testid="selection-status">{{ selectionMessage() }}</span> }</p>
          </div>
        </div>
      }

      @for (e of engines(); track e.id) {
        <h3 class="engine-heading">{{ e.name }}</h3>
        <app-engine-form [engine]="e" [removable]="engines().length > 1" (saved)="replace($event)" (removed)="load()" />
      }
      @if (adding(); as template) {
        <h3 class="engine-heading">New engine</h3>
        <app-engine-form [template]="template" (saved)="added()" (removed)="adding.set(null)" />
      } @else {
        <div class="form-actions engine-add">
          <button type="button" data-testid="add-lc0" (click)="adding.set(lc0)">Add Lc0</button>
          <button type="button" data-testid="add-engine" (click)="adding.set(other)">Add other engine</button>
        </div>
      }
    </section>
  `,
})
export class EngineSettings {
  private readonly api = inject(Api);

  protected readonly engines = signal<Engine[]>([]);
  protected readonly selection = signal<EngineSelection | null>(null);
  protected readonly selectionMessage = signal('');
  /** The template of the engine being added, if any. */
  protected readonly adding = signal<EngineForm | null>(null);
  protected readonly lc0 = LC0_TEMPLATE;
  protected readonly other = OTHER_TEMPLATE;

  constructor() {
    this.load();
  }

  protected load() {
    forkJoin([this.api.getEngines(), this.api.getEngineSelection()]).subscribe(([engines, selection]) => {
      this.engines.set(engines);
      this.selection.set(selection);
    });
  }

  protected replace(saved: Engine) {
    this.engines.update((list) => list.map((e) => (e.id === saved.id ? saved : e)));
  }

  protected added() {
    this.adding.set(null);
    this.load();
  }

  protected select(use: keyof EngineSelection, event: Event) {
    const current = this.selection();
    if (!current) return;
    const next = { ...current, [use]: Number((event.target as HTMLSelectElement).value) };
    this.api.saveEngineSelection(next).subscribe({
      next: (s) => {
        this.selection.set(s);
        this.selectionMessage.set('Saved');
      },
      error: (e) => this.selectionMessage.set(problemMessage(e)),
    });
  }
}
