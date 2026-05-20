import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ApiErrorReportService } from './api-error-report.service';

const mutating = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

export const httpErrorReportInterceptor: HttpInterceptorFn = (req, next) => {
  const reporter = inject(ApiErrorReportService);
  return next(req).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse && mutating.has(req.method.toUpperCase())) {
        reporter.present(err);
      }
      return throwError(() => err);
    }),
  );
};
