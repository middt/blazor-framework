import { inject, Injectable } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

/** Toast notifications (like INotification / Radzen NotificationService in the Blazor frontend). */
@Injectable({ providedIn: 'root' })
export class NotifyService {
  readonly #snackBar = inject(MatSnackBar);

  success(title: string, detail?: string | null): void {
    this.#open(title, detail, 'middt-snack-success', 3000);
  }

  info(title: string, detail?: string | null): void {
    this.#open(title, detail, 'middt-snack-info', 4000);
  }

  error(title: string, detail?: string | null): void {
    this.#open(title, detail, 'middt-snack-error', 7000);
  }

  #open(title: string, detail: string | null | undefined, panelClass: string, duration: number): void {
    this.#snackBar.open(detail ? `${title}: ${detail}` : title, 'Kapat', { duration, panelClass, politeness: panelClass === 'middt-snack-error' ? 'assertive' : 'polite' });
  }
}
