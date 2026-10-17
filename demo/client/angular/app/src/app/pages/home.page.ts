import { ChangeDetectionStrategy, Component } from '@angular/core';
import { SignalRDemoComponent } from '../components/signalr-demo.component';

@Component({
  selector: 'app-home',
  imports: [SignalRDemoComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="page-title">Middt Framework Sample (Angular)</h1>
    <p>Sol menüden JSON tabanlı İş Akışlarını (Müşteri KYC, Kredi Tahsis &amp; 4-Göz, Sipariş &amp; Ödeme, SLA Yönetimi) veya JSON ekran yerleşimlerini açabilirsiniz. Bu ekranlar Blazor örneğiyle aynı JSON yerleşimlerdir.</p>
    <!-- SignalR client (+ forms) is a lazy chunk: loaded once the browser is idle. -->
    @defer (on idle) {
      <app-signalr-demo />
    } @placeholder {
      <p class="sr-only">SignalR yükleniyor</p>
    }
  `,
})
export class HomePage {}
