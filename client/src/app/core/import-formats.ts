/**
 * Labels + values for ingest/import format dropdowns.
 * Values must match `SmartNarrator.Domain.Enums.SourceFormat` names (ASP.NET binds these strings).
 */
export const IMPORT_SOURCE_FORMAT_OPTIONS = [
  { value: 'PlainText', label: 'Plain text' },
  { value: 'Markdown', label: 'Markdown' },
  { value: 'Html', label: 'HTML' },
  { value: 'Pdf', label: 'PDF' },
  { value: 'Epub', label: 'EPUB (.epub)' },
] as const;

export type ImportSourceFormat = (typeof IMPORT_SOURCE_FORMAT_OPTIONS)[number]['value'];

/** HTML file input hint so EPUB/PDF/HTML/etc. are easy to pick (still allows “All files”). */
export const STORY_FILE_ACCEPT =
  '.txt,.text,.md,.markdown,.html,.htm,.pdf,.epub,text/plain,text/markdown,text/html,application/pdf,application/epub+zip';

/** Lowercase extension without dot, or empty when none. */
function fileExtensionLower(fileName: string): string {
  const trimmed = fileName.trim();
  const slash = Math.max(trimmed.lastIndexOf('/'), trimmed.lastIndexOf('\\'));
  const base = slash >= 0 ? trimmed.slice(slash + 1) : trimmed;
  const dot = base.lastIndexOf('.');
  if (dot <= 0 || dot === base.length - 1) return '';
  return base.slice(dot + 1).toLowerCase();
}

/**
 * Maps a file name (extension) to an ingest format when unambiguous.
 * Returns null when unknown — callers keep the current dropdown choice.
 */
export function inferImportFormatFromFileName(fileName: string): ImportSourceFormat | null {
  switch (fileExtensionLower(fileName)) {
    case 'txt':
    case 'text':
      return 'PlainText';
    case 'md':
    case 'markdown':
    case 'mdown':
    case 'mkd':
      return 'Markdown';
    case 'html':
    case 'htm':
    case 'xhtml':
      return 'Html';
    case 'pdf':
      return 'Pdf';
    case 'epub':
      return 'Epub';
    default:
      return null;
  }
}

/**
 * Prefer extension from {@link inferImportFormatFromFileName}; fall back to MIME when the browser supplies one.
 * Avoids guessing `text/plain` (often wrong vs Markdown).
 */
export function inferImportFormatFromFile(file: File): ImportSourceFormat | null {
  const byName = inferImportFormatFromFileName(file.name);
  if (byName !== null) return byName;

  const mime = (file.type ?? '').trim().toLowerCase();
  switch (mime) {
    case 'text/html':
      return 'Html';
    case 'text/markdown':
      return 'Markdown';
    case 'application/pdf':
      return 'Pdf';
    case 'application/epub+zip':
    case 'application/epub':
      return 'Epub';
    default:
      return null;
  }
}

/** Uses URL pathname extension when recognized (sync with server ingest inference). */
export function inferImportFormatFromUrl(urlString: string): ImportSourceFormat | null {
  const trimmed = urlString.trim();
  if (!trimmed) return null;
  try {
    const u = new URL(trimmed);
    return inferImportFormatFromFileName(u.pathname);
  } catch {
    return null;
  }
}
