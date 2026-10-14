// Display widgets – same markup and CSS classes as the Blazor display widgets and the static UiHtmlRenderer.

import { ChangeDetectionStrategy, Component, computed, forwardRef, inject } from '@angular/core';
import { Router } from '@angular/router';
import { PortableExpressionError, UiExpressions, UiFieldJson, UiUrl } from '@middt/ui-core';
import { UiNodeComponent, UiNodeListComponent } from './node.component';
import { cloneJson, isJsonObject, UiProps, UiWidgetBase } from './widget-base';

const contents = { style: 'display: contents' };

@Component({
  selector: 'middt-ui-heading',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: contents,
  template: `
    @switch (level()) {
      @case (1) { <h1 class="middt-ui-heading">{{ text('text') }}</h1> }
      @case (2) { <h2 class="middt-ui-heading">{{ text('text') }}</h2> }
      @case (3) { <h3 class="middt-ui-heading">{{ text('text') }}</h3> }
      @case (4) { <h4 class="middt-ui-heading">{{ text('text') }}</h4> }
      @case (5) { <h5 class="middt-ui-heading">{{ text('text') }}</h5> }
      @default { <h6 class="middt-ui-heading">{{ text('text') }}</h6> }
    }
  `,
})
export class UiHeadingWidget extends UiWidgetBase {
  protected readonly level = computed(() => Math.min(6, Math.max(1, UiProps.int(this.node(), 'level', 2))));
}

@Component({
  selector: 'middt-ui-text',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: contents,
  template: `<p class="middt-ui-text middt-ui-text-{{ stringProp('style') ?? 'normal' }}">{{ text('text') }}</p>`,
})
export class UiTextWidget extends UiWidgetBase {}

@Component({
  selector: 'middt-ui-fields',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: contents,
  template: `
    <dl class="middt-ui-fields">
      @for (item of items(); track $index) {
        <div class="middt-ui-field">
          <dt>{{ item.label }}</dt>
          <dd>{{ item.value }}</dd>
        </div>
      }
    </dl>
  `,
})
export class UiFieldsWidget extends UiWidgetBase {
  protected readonly items = computed(() => {
    const context = this.context();
    return (UiProps.array(this.node(), 'items') ?? [])
      .filter(isJsonObject)
      .filter(item => context.isVisible(UiFieldJson.getString(item, 'visibleWhen')))
      .map(item => ({
        label: UiFieldJson.getString(item, 'label') ?? '',
        value: context.text(UiFieldJson.getString(item, 'value'), UiFieldJson.getString(item, 'format')),
      }));
  });
}

@Component({
  selector: 'middt-ui-alert',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: contents,
  template: `<div class="middt-ui-alert middt-ui-alert-{{ severity() }}" [attr.role]="severity() === 'danger' || severity() === 'warning' ? 'alert' : 'status'">{{ text('text') }}</div>`,
})
export class UiAlertWidget extends UiWidgetBase {
  protected readonly severity = computed(() => UiProps.string(this.node(), 'severity') ?? 'info');
}

@Component({
  selector: 'middt-ui-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: contents,
  template: `<span class="middt-ui-badge middt-ui-badge-{{ stringProp('color') ?? 'secondary' }}">{{ text('text') }}</span>`,
})
export class UiBadgeWidget extends UiWidgetBase {}

@Component({
  selector: 'middt-ui-section',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [forwardRef(() => UiNodeListComponent)],
  host: contents,
  template: `
    <section class="middt-ui-section" [attr.aria-labelledby]="hasTitle() ? elementId('title') : null">
      @if (hasTitle()) {
        <h3 class="middt-ui-section-title" [id]="elementId('title')">{{ text('title') }}</h3>
      }
      <middt-ui-node-list [nodes]="node().children" [context]="context()" />
    </section>
  `,
})
export class UiSectionWidget extends UiWidgetBase {
  protected readonly hasTitle = computed(() => UiProps.string(this.node(), 'title') !== null);
}

@Component({
  selector: 'middt-ui-columns',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [forwardRef(() => UiNodeComponent)],
  host: contents,
  template: `
    <div class="middt-ui-columns">
      @for (child of node().children ?? []; track $index) {
        <div class="middt-ui-column" [style.flex]="widths()[$index] + ' 1 0'">
          <middt-ui-node [node]="child" [context]="context()" />
        </div>
      }
    </div>
  `,
})
export class UiColumnsWidget extends UiWidgetBase {
  /** Flex grow factor per child. */
  protected readonly widths = computed(() => {
    const node = this.node();
    const widths = UiProps.array(node, 'widths');
    return (node.children ?? []).map((_, index) => {
      const value = widths !== null && index < widths.length ? widths[index] : null;
      const width = typeof value === 'string' || typeof value === 'number' ? UiFieldJson.toDecimal(value) : null;
      return width !== null && width > 0 ? String(width) : '1';
    });
  });
}

@Component({
  selector: 'middt-ui-divider',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: contents,
  template: `<hr class="middt-ui-divider" />`,
})
export class UiDividerWidget extends UiWidgetBase {}

const MAX_LIST_ITEMS = 1000;

@Component({
  selector: 'middt-ui-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [forwardRef(() => UiNodeListComponent)],
  host: contents,
  template: `
    @if (items().length === 0) {
      <p class="middt-ui-text middt-ui-text-muted">{{ stringProp('empty') ?? 'Kayıt yok.' }}</p>
    } @else {
      <ul class="middt-ui-list">
        @for (item of items(); track $index) {
          <li><middt-ui-node-list [nodes]="node().children" [context]="item" /></li>
        }
      </ul>
    }
  `,
})
export class UiListWidget extends UiWidgetBase {
  /** One context per item (`row`). */
  protected readonly items = computed(() => {
    const context = this.context();
    const source = UiProps.string(this.node(), 'source');
    if (source === null) {
      return [];
    }

    let value: unknown;
    try {
      value = context.render(`{{ ${source} }}`);
    } catch (error) {
      if (error instanceof PortableExpressionError) {
        return [];
      }

      throw error;
    }

    return Array.isArray(value) ? value.slice(0, MAX_LIST_ITEMS).map(item => context.with(UiExpressions.row, cloneJson(item))) : [];
  });
}

@Component({
  selector: 'middt-ui-link',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: contents,
  template: `
    @if (safe()) {
      <a [class]="stringProp('style') === 'button' ? 'middt-ui-button' : 'middt-ui-link'" [attr.href]="href()" (click)="navigate($event)">{{ text('text') }}</a>
    } @else {
      <span class="middt-ui-link-disabled">{{ text('text') }}</span>
    }
  `,
})
export class UiLinkWidget extends UiWidgetBase {
  readonly #router = inject(Router);

  protected readonly href = computed(() => this.text('href'));
  protected readonly safe = computed(() => UiUrl.isSafe(this.href()));

  /** Local links navigate inside the application (like Blazor's link interception); others load normally. */
  protected navigate(event: MouseEvent): void {
    const href = this.href();
    if (UiUrl.isLocal(href) && event.button === 0 && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey) {
      event.preventDefault();
      void this.#router.navigateByUrl(href);
    }
  }
}
