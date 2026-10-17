import { Injectable } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';

/** Turkish paginator texts. */
@Injectable()
export class UiPaginatorIntl extends MatPaginatorIntl {
  override itemsPerPageLabel = 'Sayfa başına';
  override nextPageLabel = 'Sonraki sayfa';
  override previousPageLabel = 'Önceki sayfa';
  override firstPageLabel = 'İlk sayfa';
  override lastPageLabel = 'Son sayfa';
  override getRangeLabel = (page: number, pageSize: number, length: number): string => {
    const pages = Math.max(1, Math.ceil(length / pageSize));
    return `Sayfa ${Math.min(page + 1, pages)} / ${pages} (${length} kayıt)`;
  };
}

