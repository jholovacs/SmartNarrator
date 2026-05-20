import { HttpErrorResponse } from '@angular/common/http';

/** Matches SmartNarrator.Api `ApiClientErrorDto` (camelCase JSON). */
export interface ApiClientErrorShape {
  title?: string;
  detail?: string;
  stackTrace?: string;
  exceptionType?: string;
}

export function serializeErrorBody(body: unknown): string {
  if (body === null || body === undefined) return '(empty response body)';
  if (typeof body === 'string') return body;
  try {
    return JSON.stringify(body, null, 2);
  } catch {
    return String(body);
  }
}

export function parseApiClientError(body: unknown): ApiClientErrorShape | null {
  if (!body || typeof body !== 'object') return null;
  const o = body as Record<string, unknown>;
  const title = o['title'];
  const detail = o['detail'];
  const stackTrace = o['stackTrace'];
  const exceptionType = o['exceptionType'];
  const hasAny =
    typeof title === 'string' ||
    typeof detail === 'string' ||
    typeof stackTrace === 'string' ||
    typeof exceptionType === 'string';
  if (!hasAny) return null;
  return {
    title: typeof title === 'string' ? title : undefined,
    detail: typeof detail === 'string' ? detail : undefined,
    stackTrace: typeof stackTrace === 'string' ? stackTrace : undefined,
    exceptionType: typeof exceptionType === 'string' ? exceptionType : undefined,
  };
}

/** ASP.NET validation ProblemDetails sometimes uses `errors` map. */
export function parseValidationErrors(body: unknown): Record<string, string[]> | null {
  if (!body || typeof body !== 'object') return null;
  const o = body as Record<string, unknown>;
  const errs = o['errors'];
  if (!errs || typeof errs !== 'object') return null;
  const errMap = errs as Record<string, unknown>;
  const out: Record<string, string[]> = {};
  for (const [k, v] of Object.entries(errMap)) {
    if (Array.isArray(v) && v.every((x) => typeof x === 'string')) out[k] = v as string[];
  }
  return Object.keys(out).length ? out : null;
}

export function formatHttpErrorReport(err: HttpErrorResponse): string {
  const lines: string[] = [];
  lines.push('SmartNarrator API error report');
  lines.push('='.repeat(48));
  lines.push(`HTTP ${err.status} ${err.statusText || ''}`.trim());
  lines.push(`URL: ${err.url ?? '(unknown)'}`);
  lines.push('');
  lines.push('--- Response body (raw) ---');
  const rawBody = serializeErrorBody(err.error);
  lines.push(rawBody);
  lines.push('');

  if (rawBody.trimStart().startsWith('<')) {
    lines.push('--- Note ---');
    lines.push(
      'This looks like HTML from nginx, not JSON from the SmartNarrator API. Typical causes:\n' +
        '  • HTTP 502: nginx cached an old Docker IP for `api` after `docker compose up`/recreate — recreate spa too (`docker compose up -d --force-recreate spa`) or rely on nginx/docker.conf resolver + variable proxy_pass.\n' +
        '  • Reverse-proxy timeout while uploading (nginx/docker.conf uses 3600s send/read timeouts on /api/).\n' +
        '  • The api container crashed or refused the connection — run: docker compose logs api spa\n' +
        '  • Recreate spa after nginx.conf changes: make deploy',
    );
    lines.push('');
  }

  const parsed = parseApiClientError(err.error);
  if (parsed) {
    lines.push('--- Parsed SmartNarrator.Api.ApiClientErrorDto ---');
    if (parsed.title) lines.push(`Title: ${parsed.title}`);
    if (parsed.detail) lines.push(`Detail: ${parsed.detail}`);
    if (parsed.exceptionType) lines.push(`ExceptionType: ${parsed.exceptionType}`);
    if (parsed.stackTrace) {
      lines.push('');
      lines.push('--- Stack trace / exception dump ---');
      lines.push(parsed.stackTrace);
    }
    lines.push('');
  }

  const validation = parseValidationErrors(err.error);
  if (validation) {
    lines.push('--- Validation errors ---');
    lines.push(JSON.stringify(validation, null, 2));
    lines.push('');
  }

  const pd = err.error;
  if (pd && typeof pd === 'object') {
    const bag = pd as Record<string, unknown>;
    const traceId = bag['traceId'] ?? bag['trace_id'];
    if (typeof traceId === 'string' && traceId.length > 0) {
      lines.push(`traceId: ${traceId}`);
      lines.push('');
    }
  }

  lines.push('--- Client message ---');
  lines.push(err.message);
  return lines.join('\n');
}

export function shortHttpErrorHeadline(err: HttpErrorResponse): string {
  const parsed = parseApiClientError(err.error);
  if (parsed?.detail) return `${err.status}: ${parsed.detail}`;
  if (parsed?.title) return `${err.status}: ${parsed.title}`;
  if (typeof err.error === 'string' && err.error.trim()) return `${err.status}: ${err.error.trim()}`;
  return `${err.status} ${err.statusText || 'Request failed'}`.trim();
}
