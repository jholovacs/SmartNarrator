import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { formatHttpErrorReport, shortHttpErrorHeadline } from './http-error-report';

export interface ApiErrorDialogModel {
  readonly headline: string;
  readonly fullReport: string;
}

@Injectable({ providedIn: 'root' })
export class ApiErrorReportService {
  private readonly model = signal<ApiErrorDialogModel | null>(null);

  readonly dialog = this.model.asReadonly();

  present(err: HttpErrorResponse): void {
    this.model.set({
      headline: shortHttpErrorHeadline(err),
      fullReport: formatHttpErrorReport(err),
    });
  }

  dismiss(): void {
    this.model.set(null);
  }
}
