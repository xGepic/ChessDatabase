import { Component, inject, signal } from '@angular/core';
import { ANALYSIS_SECONDS, Api, BackupStatus, Engine, STOCKFISH_ID, problemMessage } from '../api/api';
import { sameName } from '../shared/names';

@Component({
  selector: 'app-settings-page',
  templateUrl: './settings-page.html',
})
export class SettingsPage {
  private readonly api = inject(Api);
  protected readonly ownerName = signal('');
  protected readonly aliases = signal<string[]>([]);
  protected readonly hasLichessToken = signal(false);
  protected readonly lichessMessage = signal('');
  protected readonly status = signal('');
  protected readonly engine = signal<Engine | null>(null);
  protected readonly engineMessage = signal('');
  protected readonly engineSaving = signal(false);
  protected readonly backup = signal<BackupStatus | null>(null);
  protected readonly backupMessage = signal('');
  protected readonly backingUp = signal(false);
  private saveTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    this.api.getSettings().subscribe((s) => {
      this.ownerName.set(s.ownerName);
      this.aliases.set(s.aliases);
      this.hasLichessToken.set(s.hasLichessToken);
    });
    this.api.backupStatus().subscribe((b) => this.backup.set(b));
    this.api.getEngines().subscribe((list) => this.engine.set(list.find((e) => e.id === STOCKFISH_ID) ?? null));
  }

  /** Saving checks the program by starting it once, so it happens on the button, not on every key. */
  protected readonly analysisSeconds = ANALYSIS_SECONDS;

  protected saveEngine(path: string, threads: string, hashMb: string, multiPv: string, limit: string, seconds: string) {
    const engine = this.engine();
    if (!engine) return;
    this.engineSaving.set(true);
    this.engineMessage.set('');
    this.api
      .saveEngine({
        id: engine.id,
        path,
        threads: Number(threads),
        hashMb: Number(hashMb),
        multiPv: Number(multiPv),
        interactiveSecondsLimit: limit.trim() ? Number(limit) : null,
        analysisSecondsPerMove: Number(seconds),
      })
      .subscribe({
        next: (saved) => {
          this.engine.set(saved);
          this.engineSaving.set(false);
          this.engineMessage.set(saved.reportedName ? `Saved. Found ${saved.reportedName}.` : 'Saved');
        },
        error: (e) => {
          this.engineSaving.set(false);
          this.engineMessage.set(problemMessage(e));
        },
      });
  }

  protected setBackupFolder(value: string) {
    const folder = value.trim();
    const current = this.backup();
    if (current && (current.isDefault ? folder === '' : folder === current.folder)) return;
    this.api.setBackupFolder(folder).subscribe({
      next: (b) => {
        this.backup.set(b);
        this.backupMessage.set('Saved');
      },
      error: (e) => this.backupMessage.set(problemMessage(e)),
    });
  }

  protected backUpNow() {
    this.backingUp.set(true);
    this.backupMessage.set('');
    this.api.backUpNow().subscribe({
      next: (b) => {
        this.backup.set(b);
        this.backingUp.set(false);
        this.backupMessage.set('Backed up');
      },
      error: (e) => {
        this.backingUp.set(false);
        this.backupMessage.set(problemMessage(e));
      },
    });
  }

  protected lastBackup(b: BackupStatus): string {
    return b.lastBackupAt ? new Date(b.lastBackupAt).toLocaleString() : 'none yet';
  }

  protected onNameInput(value: string) {
    this.ownerName.set(value);
    this.status.set('');
    clearTimeout(this.saveTimer);
    this.saveTimer = setTimeout(() => this.save(), 400);
  }

  protected addAlias(input: HTMLInputElement) {
    const alias = input.value.trim();
    input.value = '';
    if (!alias || this.aliases().some((a) => sameName(a, alias))) return;
    this.aliases.update((list) => [...list, alias]);
    this.save();
  }

  protected removeAlias(alias: string) {
    this.aliases.update((list) => list.filter((a) => a !== alias));
    this.save();
  }

  /** Saves (or with an empty value removes) the Lichess token. It has its own call, so the other settings are untouched. */
  protected saveLichessToken(input: HTMLInputElement, remove = false) {
    const token = remove ? '' : input.value.trim();
    if (!remove && !token) return;
    this.api.saveLichessToken(token).subscribe({
      next: (s) => {
        input.value = '';
        this.hasLichessToken.set(s.hasLichessToken);
        this.lichessMessage.set(s.hasLichessToken ? 'Token saved' : 'Token removed');
      },
      error: (e) => this.lichessMessage.set(problemMessage(e)),
    });
  }

  protected save() {
    clearTimeout(this.saveTimer);
    this.api.saveSettings({ ownerName: this.ownerName(), aliases: this.aliases() }).subscribe({
      next: (s) => {
        this.aliases.set(s.aliases);
        this.status.set('Saved');
      },
      error: (e) => this.status.set(problemMessage(e)),
    });
  }
}
