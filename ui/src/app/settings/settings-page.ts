import { Component, inject, signal } from '@angular/core';
import { Api, BackupStatus, problemMessage } from '../api/api';
import { EngineSettings } from './engine-settings';

@Component({
  selector: 'app-settings-page',
  imports: [EngineSettings],
  templateUrl: './settings-page.html',
})
export class SettingsPage {
  private readonly api = inject(Api);
  protected readonly ownerName = signal('');
  protected readonly hasLichessToken = signal(false);
  protected readonly lichessMessage = signal('');
  protected readonly status = signal('');
  protected readonly backup = signal<BackupStatus | null>(null);
  protected readonly backupMessage = signal('');
  protected readonly backingUp = signal(false);
  private saveTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    this.api.getSettings().subscribe((s) => {
      this.ownerName.set(s.ownerName);
      this.hasLichessToken.set(s.hasLichessToken);
    });
    this.api.backupStatus().subscribe((b) => this.backup.set(b));
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
    this.api.saveSettings({ ownerName: this.ownerName() }).subscribe({
      next: () => this.status.set('Saved'),
      error: (e) => this.status.set(problemMessage(e)),
    });
  }
}
