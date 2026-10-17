import { ChangeDetectionStrategy, Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { MiddtSignalRService } from '@middt/angular/signalr';
import { NotifyService } from '@middt/angular/core';

/** SignalR through the BFF: listens on /hubs/testhub (like the Blazor TestSignalRComponent) and can broadcast. */
@Component({
  selector: 'app-signalr-demo',
  imports: [FormsModule, MatCardModule, MatButtonModule, MatFormFieldModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <mat-card appearance="outlined" class="signalr">
      <mat-card-header><mat-card-title>SignalR</mat-card-title><mat-card-subtitle data-testid="signalr-state">{{ state() }}</mat-card-subtitle></mat-card-header>
      <mat-card-content>
        <p data-testid="signalr-last">Son mesaj: {{ lastMessage() ?? '–' }}</p>
        <form class="signalr-form" (ngSubmit)="send()">
          <mat-form-field appearance="outline" subscriptSizing="dynamic">
            <mat-label>Mesaj</mat-label>
            <input matInput name="message" [ngModel]="message()" (ngModelChange)="message.set($event)" maxlength="200" />
          </mat-form-field>
          <button mat-flat-button type="submit" [disabled]="!message() || state() !== 'Connected'">Herkese gönder</button>
        </form>
      </mat-card-content>
    </mat-card>
  `,
  styles: `.signalr { margin-top: 1.5rem; max-width: 640px; } .signalr-form { display: flex; gap: .75rem; align-items: center; flex-wrap: wrap; }`,
})
export class SignalRDemoComponent implements OnInit, OnDestroy {
  readonly #signalR = inject(MiddtSignalRService);
  readonly #notify = inject(NotifyService);
  #connection?: HubConnection;

  protected readonly state = signal<string>(HubConnectionState.Disconnected);
  protected readonly lastMessage = signal<string | null>(null);
  protected readonly message = signal('');

  async ngOnInit(): Promise<void> {
    this.#connection = this.#signalR.connect<string>('/hubs/testhub', message => this.lastMessage.set(message));
    this.#connection.onreconnecting(() => this.state.set(HubConnectionState.Reconnecting));
    this.#connection.onreconnected(() => this.state.set(HubConnectionState.Connected));
    this.#connection.onclose(() => this.state.set(HubConnectionState.Disconnected));
    try {
      this.state.set(HubConnectionState.Connecting);
      await this.#connection.start();
      this.state.set(HubConnectionState.Connected);
    } catch (error) {
      this.state.set(HubConnectionState.Disconnected);
      this.#notify.error('SignalR bağlantısı kurulamadı', error instanceof Error ? error.message : null);
    }
  }

  async send(): Promise<void> {
    const message = this.message();
    if (!this.#connection || !message) {
      return;
    }

    await this.#signalR.send(this.#connection, { messageType: 0, messageTo: [], data: message });
    this.message.set('');
  }

  ngOnDestroy(): void {
    void this.#connection?.stop();
  }
}
