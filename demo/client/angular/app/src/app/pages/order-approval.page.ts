import { ChangeDetectionStrategy, Component, signal, viewChild } from '@angular/core';
import { MiddtWorkflowInstanceDetailComponent, MiddtWorkflowInstanceListComponent } from '@middt/angular';
import type { WorkflowInstanceModel } from '@middt/ui-core';

/** Order approval workflow: instance list and detail side by side (counterpart of OrderApprovalPage.razor). */
@Component({
  selector: 'app-order-approval',
  imports: [MiddtWorkflowInstanceListComponent, MiddtWorkflowInstanceDetailComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="page-title sr-only">Sipariş onayları</h1>
    <div class="middt-wf-layout">
      <middt-workflow-instance-list #list [workflow]="workflowKey" title="Sipariş onayları" [selectedId]="selectedId()" (selected)="onSelected($event)" />
      <middt-workflow-instance-detail [workflow]="workflowKey" [instanceId]="selectedId()" (changed)="onChanged()" />
    </div>
  `,
  styles: `
    .middt-wf-layout {
      display: grid;
      /* the detail column hosts wizard step forms: keep it at least ~460px wide next to the list */
      grid-template-columns: minmax(0, 1fr) minmax(min(100%, 460px), 1fr);
      gap: 1rem;
      align-items: start;
    }

    @media (max-width: 1100px) {
      .middt-wf-layout { grid-template-columns: minmax(0, 1fr); }
    }
  `,
})
export class OrderApprovalPage {
  protected readonly workflowKey = 'order-approval';
  protected readonly selectedId = signal<string | null>(null);
  private readonly list = viewChild.required<MiddtWorkflowInstanceListComponent>('list');

  protected onSelected(instance: WorkflowInstanceModel): void {
    this.selectedId.set(instance.id);
  }

  // A transition changes state/status in the list as well.
  protected onChanged(): void {
    void this.list().refresh();
  }
}
