import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { AnalysisUpdate } from '../api/api';

/**
 * Interactive Analysis for one Game view. Talks to the backend's analysis hub: each analyse()
 * restarts the Engine on a new position, stop() ends the Engine process. Leaving the view closes
 * the connection, which stops the Engine too.
 */
@Injectable()
export class EngineSession {
  /** The latest lines for the position last asked about. */
  readonly update = signal<AnalysisUpdate | null>(null);
  readonly error = signal('');
  readonly on = signal(false);

  private connection: HubConnection | null = null;
  private requestId = 0;

  constructor() {
    inject(DestroyRef).onDestroy(() => void this.close());
  }

  /** Analyses the position after these moves (UCI, from the start of the Game). */
  async analyse(moves: string[]) {
    const id = ++this.requestId;
    this.update.set(null);
    this.error.set('');
    try {
      const connection = await this.connect();
      if (id !== this.requestId || !this.on()) return;
      await connection.invoke('Analyse', id, moves);
    } catch (e) {
      if (id !== this.requestId) return;
      this.error.set(hubMessage(e));
      this.on.set(false);
    }
  }

  async start(moves: string[]) {
    this.on.set(true);
    await this.analyse(moves);
  }

  async stop() {
    this.on.set(false);
    this.requestId++;
    this.update.set(null);
    if (this.connection?.state === HubConnectionState.Connected) await this.connection.invoke('Stop').catch(() => {});
  }

  private async connect(): Promise<HubConnection> {
    if (this.connection?.state === HubConnectionState.Connected) return this.connection;
    if (!this.connection) {
      this.connection = new HubConnectionBuilder().withUrl('/hubs/analysis').configureLogging(LogLevel.Warning).build();
      this.connection.on('lines', (update: AnalysisUpdate) => {
        if (update.requestId === this.requestId && this.on()) this.update.set(update);
      });
      this.connection.on('engineError', (message: string) => {
        this.error.set(message);
        this.on.set(false);
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

/** The message of a HubException from the server, without SignalR's wrapping. */
function hubMessage(error: unknown): string {
  const text = error instanceof Error ? error.message : String(error);
  const marker = 'HubException: ';
  const at = text.indexOf(marker);
  if (at >= 0) return text.slice(at + marker.length);
  return 'The Engine could not be reached.';
}
