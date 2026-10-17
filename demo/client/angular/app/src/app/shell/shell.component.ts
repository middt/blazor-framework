import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { BreakpointObserver } from '@angular/cdk/layout';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '@middt/angular/core';
import { MenuService } from './menu.service';

/** Stored theme choice; storage may be unavailable (blocked site data, private mode). */
function storedTheme(): string | null {
  try {
    return localStorage.getItem('middt-theme');
  } catch {
    return null;
  }
}

@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatDividerModule, MatToolbarModule, MatSidenavModule, MatListModule, MatIconModule, MatButtonModule, MatMenuModule, MatTooltipModule, RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <mat-toolbar class="shell-toolbar">
      <button mat-icon-button (click)="sidenavOpen.set(!sidenavOpen())" aria-label="Menüyü aç/kapat"><mat-icon>menu</mat-icon></button>
      <span class="shell-title">Middt Framework Angular Sample</span>
      <span class="shell-spacer"></span>
      <button mat-icon-button (click)="toggleTheme()" [matTooltip]="dark() ? 'Açık tema' : 'Koyu tema'" aria-label="Temayı değiştir">
        <mat-icon>{{ dark() ? 'light_mode' : 'dark_mode' }}</mat-icon>
      </button>
      <button mat-button [matMenuTriggerFor]="userMenu" class="shell-user"><mat-icon>account_circle</mat-icon> {{ auth.name() }}</button>
      <mat-menu #userMenu="matMenu">
        <button mat-menu-item (click)="auth.logout()"><mat-icon>logout</mat-icon> Çıkış</button>
      </mat-menu>
    </mat-toolbar>
    <mat-sidenav-container class="shell-container">
      <mat-sidenav [mode]="mobile() ? 'over' : 'side'" [opened]="sidenavOpen()" (closedStart)="sidenavOpen.set(false)" class="shell-sidenav">
        <mat-nav-list aria-label="Ana menü">
          <a mat-list-item routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }"><mat-icon matListItemIcon>home</mat-icon><span matListItemTitle>Home</span></a>
          <div mat-subheader class="shell-menu-group">
            <mat-icon aria-hidden="true">{{ menu.workflowGroup.icon }}</mat-icon>{{ menu.workflowGroup.name }}
          </div>
          @for (item of menu.workflowGroup.items; track item.path) {
            <a mat-list-item class="shell-menu-child" [routerLink]="item.path" routerLinkActive="active"><mat-icon matListItemIcon>{{ item.icon }}</mat-icon><span matListItemTitle>{{ item.name }}</span></a>
          }
          <mat-divider />
          @for (item of menu.layoutItems(); track item.path) {
            <a mat-list-item [routerLink]="item.path" routerLinkActive="active"><mat-icon matListItemIcon>{{ item.icon }}</mat-icon><span matListItemTitle>{{ item.name }}</span></a>
          }
          @for (item of menu.fixedItems(); track item.path) {
            <a mat-list-item [routerLink]="item.path" routerLinkActive="active"><mat-icon matListItemIcon>{{ item.icon }}</mat-icon><span matListItemTitle>{{ item.name }}</span></a>
          }
        </mat-nav-list>
      </mat-sidenav>
      <mat-sidenav-content>
        <main class="shell-main">
          <router-outlet />
        </main>
        <footer class="shell-footer">Middt Framework, Copyright © {{ year }}</footer>
      </mat-sidenav-content>
    </mat-sidenav-container>
  `,
  styles: `
    :host { display: flex; flex-direction: column; height: 100vh; }
    .shell-toolbar { background: var(--mat-sys-primary); color: var(--mat-sys-on-primary); gap: .5rem; }
    .shell-toolbar button { color: inherit; }
    .shell-title { font: var(--mat-sys-title-medium); }
    .shell-spacer { flex: 1; }
    .shell-container { flex: 1; }
    .shell-sidenav { width: 240px; }
    .shell-main { padding: 1.5rem; }
    .shell-footer { padding: .75rem 1.5rem; color: var(--mat-sys-on-surface-variant); font: var(--mat-sys-body-small); }
    a.active { background: var(--mat-sys-secondary-container); }
    .shell-menu-group { display: flex; align-items: center; gap: .5rem; }
    .shell-menu-group mat-icon { font-size: 20px; width: 20px; height: 20px; }
    .shell-menu-child { padding-left: 1.5rem; }
  `,
})
export class ShellComponent implements OnInit {
  protected readonly auth = inject(AuthService);
  protected readonly menu = inject(MenuService);
  protected readonly sidenavOpen = signal(true);
  protected readonly mobile = signal(false);
  protected readonly dark = signal(storedTheme() === 'dark');
  protected readonly year = new Date().getFullYear();

  constructor() {
    inject(BreakpointObserver).observe('(max-width: 800px)').pipe(takeUntilDestroyed()).subscribe(state => {
      this.mobile.set(state.matches);
      this.sidenavOpen.set(!state.matches);
    });
    this.#applyTheme();
  }

  async ngOnInit(): Promise<void> {
    await this.menu.refresh();
  }

  protected toggleTheme(): void {
    this.dark.set(!this.dark());
    try {
      localStorage.setItem('middt-theme', this.dark() ? 'dark' : 'light');
    } catch {
      // storage unavailable (private mode): the choice lasts for this page only
    }

    this.#applyTheme();
  }

  #applyTheme(): void {
    document.documentElement.classList.toggle('dark', this.dark());
  }
}
