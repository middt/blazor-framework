import { EnvironmentProviders } from '@angular/core';
import { provideWorkflowViews } from '@middt/angular/workflow';
import { OrderResultView } from './order-result.view';
import { OrderReviewView } from './order-review.view';
import { RejectReasonForm } from './reject-reason.form';

/**
 * Registers the sample's Angular workflow views – the counterparts of the Razor views in Middt.Sample.Views, referenced by
 * order-approval.json / order-approval.v2.json (`"view": { "component": "…" }`).
 */
export function provideSampleViews(): EnvironmentProviders {
  return provideWorkflowViews([
    { name: 'order-review', kind: 'state', component: OrderReviewView },
    { name: 'order-result', kind: 'state', component: OrderResultView },
    { name: 'reject-reason', kind: 'transition', component: RejectReasonForm },
  ]);
}
