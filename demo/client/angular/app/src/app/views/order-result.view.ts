import { ChangeDetectionStrategy, Component, computed } from '@angular/core';
import { WorkflowStateViewBase } from '@middt/angular';
import { SAMPLE_VIEW_STYLES, ViewFormat } from './view-format';

/** State view of the final states: the decision. */
@Component({
  selector: 'app-order-result-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="sv-view" aria-labelledby="sv-result-title">
      <header class="sv-header">
        <h3 id="sv-result-title">Sipariş #{{ text('orderId') }} · {{ text('customerName') }}</h3>
        <span [class]="approved() ? 'sv-badge sv-good' : 'sv-badge sv-bad'">{{ approved() ? 'Onaylandı' : 'Reddedildi' }}</span>
      </header>

      <dl class="sv-grid">
        <dt>Tutar</dt>
        <dd class="sv-amount">{{ format.money(number('amount')) }}</dd>
        @if (has('product')) {
          <dt>Ürün grubu</dt>
          <dd>{{ text('product') }}</dd>
        }
        @if (has('deliveryType')) {
          <dt>Teslimat</dt>
          <dd data-field="deliveryType"><span [class]="text('deliveryType', '') === 'Ekspres' ? 'sv-badge sv-express' : 'sv-badge'">{{ text('deliveryType') }}</span>
            @if (has('review.changedBy')) {
              <small class="sv-muted"> ({{ text('review.changedBy') }} tarafından standarda çevrildi)</small>
            }
          </dd>
        }
        @if (has('customerType')) {
          <dt>Müşteri</dt>
          <dd>{{ text('customerType') }}{{ customerSuffix() }}</dd>
        }
        @if (reviewText(); as review) {
          <dt>Onay seviyesi</dt>
          <dd>{{ review }}</dd>
        }
        <dt>Karar veren</dt>
        <dd>{{ decidedBy() }}</dd>
        <dt>Karar zamanı</dt>
        <dd>{{ format.date(date('review.decidedAt') ?? date('approvedAt')) }}</dd>
        @if (approved()) {
          <dt>Açıklama</dt>
          <dd>{{ text('review.comment') }}</dd>
        } @else {
          <dt>Red nedeni</dt>
          <dd>{{ rejectReason() }}</dd>
        }
      </dl>
    </article>
  `,
  styles: SAMPLE_VIEW_STYLES,
})
export class OrderResultView extends WorkflowStateViewBase {
  protected readonly format = ViewFormat;
  protected readonly approved = computed(() => this.instance().state === 'approved');

  protected readonly decidedBy = computed(() =>
    this.approved() ? this.text('review.approvedBy', this.text('review.changedBy', 'Otomatik onay')) : this.text('review.rejectedBy', '-'),
  );

  protected readonly reviewText = computed(() => {
    const level = this.text('review.level', '');
    if (level === 'finance') {
      return 'Finans onayı';
    }

    if (level.length > 0) {
      return level;
    }

    return this.has('review.changedBy') ? 'Ekspres teslimat onayı' : null;
  });

  protected readonly customerSuffix = computed(() => {
    if (this.has('company.taxNumber')) {
      return ` · VKN ${this.text('company.taxNumber')}`;
    }

    return this.has('person.identityNumber') ? ` · TCKN ${ViewFormat.maskIdentity(this.text('person.identityNumber', ''))}` : '';
  });

  protected readonly rejectReason = computed(() => {
    const score = this.text('credit.score', '');
    const value = this.number('credit.score');
    return this.text('review.reason', score.length > 0 && value !== null && value < 50 ? `Kredi skoru yetersiz (${score})` : '-');
  });

  protected has(path: string): boolean {
    return this.text(path, '').length > 0;
  }
}
