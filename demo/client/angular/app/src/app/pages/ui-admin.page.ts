import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MiddtLayoutEditorComponent } from '@middt/angular';
import { MenuService } from '../shell/menu.service';

/** Runtime layout editor; saving or deleting a layout refreshes the side menu (pages and menu entries change at runtime). */
@Component({
  selector: 'app-ui-admin',
  imports: [MiddtLayoutEditorComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { style: 'display: contents' },
  template: `<middt-layout-editor (changed)="refreshMenu()" />`,
})
export class UiAdminPage {
  readonly #menu = inject(MenuService);

  protected refreshMenu(): void {
    void this.#menu.refresh();
  }
}
