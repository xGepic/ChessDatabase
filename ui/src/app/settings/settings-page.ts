import { Component, inject, signal } from '@angular/core';
import { Api, BackupStatus, problemMessage } from '../api/api';
import { sameName } from '../shared/names';

@Component({
  selector: 'app-settings-page',
  templateUrl: './settings-page.html',
})
export class SettingsPage {
  private readonly api = inject(Api);
  protected readonly ownerName = signal('');
  protected readonly aliases = signal<string[]>([]);
  protected readonly status = signal('');
  protected readonly backup = signal<BackupStatus | null>(null);
  protected readonly backupMessage = signal('');
  protected readonly backingUp = signal(false);
  private saveTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    this.api.getSettings().subscribe((s) => {
      this.ownerName.set(s.ownerName);
      this.aliases.set(s.aliases);
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
