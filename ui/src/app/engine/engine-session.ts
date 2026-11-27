import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { forkJoin, lastValueFrom } from 'rxjs';
import { AnalysisUpdate, Api, Engine } from '../api/api';

const COMPARE_KEY = 'chessdb.compareEngine';

/**
 * Interactive Analysis for one Game view. Talks to the backend's analysis hub: each analyse()
 * moves the Engine (and the one compared with it, if any) to a new position, stop() ends their
 * processes. Leaving the view closes the connection, which stops them too.
 */
@Injectable()
export class EngineSession {
  private readonly api = inject(Api);

  readonly on = signal(false);
  /** Every configured Engine, and the one Settings choose for Interactive Analysis. */
  readonly engines = signal<Engine[]>([]);
  readonly mainId = signal<number | null>(null);
  /** The Engine run side by side with the main one; null for none. Remembered between Games. */
  readonly compareId = signal<number | null>(readCompareId());

  /** The latest lines per Engine for the position last asked about. */
  private readonly updates = signal<ReadonlyMap<number, AnalysisUpdate>>(new Map());
  private readonly errors = signal<ReadonlyMap<number, string>>(new Map());
  /** An error not tied to one Engine (the hub could not be reached, illegal moves). */
  private readonly generalError = signal('');

  readonly main = computed(() => this.engines().find((e) => e.id === this.mainId()) ?? null);
  /** The compared Engine, unless it is the main one, no longer exists or has no program set. */
  readonly compared = computed(() => {
    const id = this.compareId();
    return id === this.mainId() ? null : (this.engines().find((e) => e.id === id && e.path) ?? null);
  });
  readonly update = computed(() => this.updates().get(this.mainId() ?? -1) ?? null);
  readonly error = computed(() => this.generalError() || (this.errors().get(this.mainId() ?? -1) ?? ''));
  readonly comparedUpdate = computed(() => this.updates().get(this.compared()?.id ?? -1) ?? null);
  readonly comparedError = computed(() => this.errors().get(this.compared()?.id ?? -1) ?? '');

  private connection: HubConnection | null = null;
  private requestId = 0;
  private moves: string[] = [];

  constructor() {
    inject(DestroyRef).onDestroy(() => void this.close());
    void this.loadEngines();
  }

  /** Reads the Engines and the Interactive Analysis choice again: Settings may have changed them meanwhile. */
  private async loadEngines() {
    const [engines, selection] = await lastValueFrom(forkJoin([this.api.getEngines(), this.api.getEngineSelection()]));
    this.engines.set(engines);
    this.mainId.set(selection.interactiveEngineId);
  }

  /** Analyses the position after these moves (UCI, from the start of the Game). */
  async analyse(moves: string[]) {
    this.moves = moves;
    const id = ++this.requestId;
    this.updates.set(new Map());
    this.errors.set(new Map());
    this.generalError.set('');
    try {
      const connection = await this.connect();
      if (id !== this.requestId || !this.on()) return;
      // The server answers with the Engine it ran as the main one, which is how its lines are told apart.
      const mainId = await connection.invoke<number>('Analyse', id, moves, this.compared()?.id ?? null);
      if (id === this.requestId) this.mainId.set(mainId);
    } catch (e) {
      if (id !== this.requestId) return;
      this.generalError.set(hubMessage(e));
      this.on.set(false);
    }
  }

  async start(moves: string[]) {
    this.on.set(true);
    await this.loadEngines().catch(() => {});
    await this.analyse(moves);
  }

  async stop() {
    this.on.set(false);
    this.requestId++;
    this.updates.set(new Map());
    if (this.connection?.state === HubConnectionState.Connected) await this.connection.invoke('Stop').catch(() => {});
  }

  /** Runs another Engine beside the main one (null: none). Takes effect at once when the Engine is on. */
  async compareWith(engineId: number | null) {
    this.compareId.set(engineId);
    try {
      if (engineId === null) localStorage.removeItem(COMPARE_KEY);
      else localStorage.setItem(COMPARE_KEY, String(engineId));
    } catch {
      // Storage unavailable: the choice just isn't remembered.
    }
    if (this.on()) await this.analyse(this.moves);
  }

  private async connect(): Promise<HubConnection> {
    if (this.connection?.state === HubConnectionState.Connected) return this.connection;
    if (!this.connection) {
      this.connection = new HubConnectionBuilder().withUrl('/hubs/analysis').configureLogging(LogLevel.Warning).build();
      this.connection.on('lines', (update: AnalysisUpdate) => {
        if (update.requestId === this.requestId && this.on()) this.updates.update((m) => new Map(m).set(update.engineId, update));
      });
      // The main Engine failing switches analysis off; a compared one failing only shows in its panel.
      this.connection.on('engineError', (message: string, engineId: number) => {
        this.errors.update((m) => new Map(m).set(engineId, message));
        if (engineId === this.mainId()) this.on.set(false);
      });
      this.connection.onclose(() => this.on.set(false));
    }
    if (this.connection.state === HubConnectionState.Disconnected) await this.connection.start();
    return this.connection;
  }

  private async close() {
    this.on.set(false);
    await this.connection?.stop();
    this.connection = null;
  }
}

function readCompareId(): number | null {
  try {
    const value = localStorage.getItem(COMPARE_KEY);
    return value ? Number(value) : null;
  } catch {
    return null;
  }
}

/** The message of a HubException from the server, without SignalR's wrapping. */
function hubMessage(error: unknown): string {
  const text = error instanceof Error ? error.message : String(error);
  const marker = 'HubException: ';
  const at = text.indexOf(marker);
  if (at >= 0) return text.slice(at + marker.length);
  return 'The Engine could not be reached.';
}
