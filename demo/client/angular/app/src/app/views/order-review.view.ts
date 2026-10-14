import { ChangeDetectionStrategy, Component, computed } from '@angular/core';
import { WorkflowStateViewBase } from '@middt/angular';
import { SAMPLE_VIEW_STYLES, ViewFormat } from './view-format';

/** State view of the review states (finance-review / manager-review / express-review / escalated): what a manager needs to decide. */
@Component({
  selector: 'app-order-review-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="sv-view" aria-labelledby="sv-review-title">
      <header class="sv-header">
        <h3 id="sv-review-title">Sipariş #{{ text('orderId') }} · {{ text('customerName') }}</h3>
        <span [class]="format.scoreClass(number('credit.score'))" title="Kredi skoru">Skor {{ text('credit.score') }}</span>
      </header>

      @if (level(); as level) {
        <p class="sv-level" [attr.data-level]="instance().state">{{ level }}</p>
      }

      @if (instance().state === 'escalated') {
        <p class="sv-alert" role="status">Bu sipariş süresi içinde karara bağlanmadığı için eskale edildi.</p>
      }

      @if (highAmount()) {
        <p class="sv-alert sv-alert-danger" role="status">Yüksek tutar: ek kontrol önerilir.</p>
      }

      @if (isExpress()) {
        <p class="sv-alert sv-express" role="status">Ekspres teslimat istendi.</p>
      }

      <dl class="sv-grid">
        <dt>Tutar</dt>
        <dd class="sv-amount">{{ format.money(number('amount')) }}</dd>
        @if (has('product')) {
          <dt>Ürün grubu</dt>
          <dd>{{ text('product') }}</dd>
        }
        @if (has('deliveryType')) {
          <dt>Teslimat</dt>
          <dd><span [class]="isExpress() ? 'sv-badge sv-express' : 'sv-badge'">{{ text('deliveryType') }}</span></dd>
        }
        @if (has('customerType')) {
          <dt>Müşteri tipi</dt>
          <dd>{{ text('customerType') }}</dd>
        }
        @if (has('email')) {
          <dt>E-posta</dt>
          <dd>{{ text('email') }}</dd>
        }
        <dt>Talep eden</dt>
        <dd>{{ text('requestedBy') }}</dd>
        <dt>Talep zamanı</dt>
        <dd>{{ format.date(date('requestedAt')) }}</dd>
        <dt>Kredi kontrolü</dt>
        <dd>{{ format.date(date('credit.checkedAt')) }}</dd>
      </dl>

      @if (has('company.taxNumber')) {
        <section class="sv-section" aria-labelledby="sv-review-company">
          <h4 id="sv-review-company">Firma bilgileri</h4>
          <dl class="sv-grid">
            <dt>Vergi numarası</dt>
            <dd>{{ text('company.taxNumber') }}</dd>
            <dt>Vergi dairesi</dt>
            <dd>{{ text('company.taxOffice') }}</dd>
            <dt>Yetkili kişi</dt>
            <dd>{{ text('company.contactPerson') }}</dd>
          </dl>
        </section>
      } @else if (has('person.identityNumber')) {
        <section class="sv-section" aria-labelledby="sv-review-person">
          <h4 id="sv-review-person">Kimlik bilgileri</h4>
          <dl class="sv-grid">
            <dt>T.C. kimlik no</dt>
            <dd>{{ format.maskIdentity(text('person.identityNumber', '')) }}</dd>
            <dt>Doğum tarihi</dt>
            <dd>{{ format.dateOnly(date('person.birthDate')) }}</dd>
            @if (has('person.phone')) {
              <dt>Telefon</dt>
              <dd>{{ text('person.phone') }}</dd>
            }
          </dl>
        </section>
      }

      @if (text('note', ''); as note) {
        <blockquote class="sv-note">{{ note }}</blockquote>
      }

      @if (!interactive()) {
        <p class="sv-muted">Karar için uygulamadaki işlemleri (Onayla / Reddet{{ instance().state === 'express-review' ? ' / Standart teslimata çevir' : '' }}) kullanın.</p>
      }
    </article>
  `,
  styles: SAMPLE_VIEW_STYLES,
})
export class OrderReviewView extends WorkflowStateViewBase {
  protected readonly format = ViewFormat;
  protected readonly level = computed(() => ViewFormat.reviewLevel(this.instance().state));
  protected readonly isExpress = computed(() => this.text('deliveryType', '') === 'Ekspres');
  protected readonly highAmount = computed(() => (this.number('amount') ?? 0) > 20000);

  protected has(path: string): boolean {
    return this.text(path, '').length > 0;
  }
}
