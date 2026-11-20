import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const fix = process.argv.includes('--fix');
const ts = createRequire(join(root, 'web', 'package.json'))('typescript');

const scriptExtensions = /\.(ts|mts|cts|js|mjs|cjs)$/;
const generated = ['web/projects/ardea/src/app/core/api/generated/'];
const outOfScope = ['docs/', 'node_modules/', '/node_modules/', '/dist/', 'web/e2e/baselines/'];

const suppression = /^(\/\/|\/\*|<!--)\s*(eslint-disable|eslint-enable|stylelint-disable|stylelint-enable|prettier-ignore)\b/;
const directive =
  /^(\/\/\s*@ts-check\b|\/\/\s*@ts-nocheck\b|\/\/\s*@ts-expect-error\s+\S|\/\/\/\s*<reference\s|\/\*\s*(istanbul|c8|v8) ignore\b|\/\/\s*(c8|v8) ignore\b|#!)/;

function files() {
  const output = execFileSync('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard'], { cwd: root, encoding: 'utf8' });
  return [...new Set(output.split('\0').filter(Boolean))].filter((path) => existsSync(join(root, path))).sort();
}

function classify(path) {
  if (outOfScope.some((p) => path.startsWith(p) || path.includes(p)) || generated.some((p) => path.startsWith(p))) {
    return null;
  }
  const inScope = path.startsWith('web/') || path.startsWith('site/') || path.startsWith('tests/e2e/') || path.startsWith('build/');
  if (!inScope) {
    return null;
  }
  if (scriptExtensions.test(path)) {
    return 'script';
  }
  if (path.startsWith('web/') && path.endsWith('.html')) {
    return 'html';
  }
  if (path.startsWith('web/') && path.endsWith('.scss')) {
    return 'scss';
  }
  if ((path.startsWith('web/') || path.startsWith('site/')) && path.endsWith('.css')) {
    return 'css';
  }
  return null;
}

function scriptComments(path, text) {
  const kind = path.endsWith('.ts') || path.endsWith('.mts') || path.endsWith('.cts') ? ts.ScriptKind.TS : ts.ScriptKind.JS;
  const source = ts.createSourceFile(path, text, ts.ScriptTarget.Latest, true, kind);
  const found = new Map();
  const positions = new Set([0]);
  const embedded = [];
  const visit = (node) => {
    positions.add(node.end);
    if (isComponentProperty(node, 'template') && ts.isNoSubstitutionTemplateLiteral(node.initializer)) {
      embedded.push({ kind: 'html', start: node.initializer.getStart(source) + 1, end: node.initializer.end - 1 });
    }
    if (isComponentProperty(node, 'styles')) {
      const values = ts.isArrayLiteralExpression(node.initializer) ? node.initializer.elements : [node.initializer];
      for (const value of values) {
        if (ts.isNoSubstitutionTemplateLiteral(value) || ts.isStringLiteral(value)) {
          embedded.push({ kind: 'css', start: value.getStart(source) + 1, end: value.end - 1 });
        }
      }
    }
    for (const child of node.getChildren(source)) {
      visit(child);
    }
  };
  visit(source);
  for (const position of positions) {
    for (const range of [...(ts.getLeadingCommentRanges(text, position) ?? []), ...(ts.getTrailingCommentRanges(text, position) ?? [])]) {
      found.set(range.pos, { start: range.pos, end: range.end });
    }
  }
  const comments = [...found.values()];
  for (const part of embedded) {
    comments.push(...(part.kind === 'html' ? htmlComments(text, part.start, part.end) : styleComments(text, part.start, part.end, false)));
  }
  return comments;
}

function isComponentProperty(node, name) {
  if (!ts.isPropertyAssignment(node) || !ts.isIdentifier(node.name) || node.name.text !== name) {
    return false;
  }
  const call = node.parent?.parent;
  return !!call && ts.isCallExpression(call) && ts.isIdentifier(call.expression) && call.parent && ts.isDecorator(call.parent);
}

function htmlComments(text, start = 0, end = text.length) {
  const comments = [];
  let i = text.indexOf('<!--', start);
  while (i >= 0 && i < end) {
    const close = text.indexOf('-->', i + 4);
    const stop = close < 0 || close + 3 > end ? end : close + 3;
    comments.push({ start: i, end: stop });
    i = text.indexOf('<!--', stop);
  }
  return comments;
}

function styleComments(text, start, end, lineComments) {
  const comments = [];
  let i = start;
  while (i < end) {
    const c = text[i];
    const next = text[i + 1];
    if (c === '"' || c === "'") {
      let j = i + 1;
      while (j < end && text[j] !== c) {
        j += text[j] === '\\' ? 2 : 1;
      }
      i = j + 1;
    } else if (text.startsWith('url(', i)) {
      const close = text.indexOf(')', i);
      i = close < 0 || close > end ? end : close + 1;
    } else if (c === '/' && next === '*') {
      const close = text.indexOf('*/', i + 2);
      const stop = close < 0 || close + 2 > end ? end : close + 2;
      comments.push({ start: i, end: stop });
      i = stop;
    } else if (lineComments && c === '/' && next === '/') {
      const close = text.indexOf('\n', i);
      const stop = close < 0 || close > end ? end : close;
      comments.push({ start: i, end: stop });
      i = stop;
    } else {
      i++;
    }
  }
  return comments;
}

function lineOf(text, position) {
  let line = 1;
  for (let i = 0; i < position; i++) {
    if (text.charCodeAt(i) === 10) {
      line++;
    }
  }
  return line;
}

function lineStart(text, position) {
  return text.lastIndexOf('\n', position - 1) + 1;
}

function lineEnd(text, position) {
  const end = text.indexOf('\n', position);
  return end < 0 ? text.length : end;
}

function aloneOnLine(text, comment) {
  return text.slice(lineStart(text, comment.start), comment.start).trim() === '' && text.slice(comment.end, lineEnd(text, comment.end)).trim() === '';
}

function allowed(text, comment, comments) {
  const body = text.slice(comment.start, comment.end).trim();
  if (directive.test(body) || suppression.test(body)) {
    return true;
  }
  if (!aloneOnLine(text, comment)) {
    return false;
  }
  let next = comment;
  for (;;) {
    const following = lineEnd(text, next.end) + 1;
    const candidate = comments.find((c) => c.start >= following && lineStart(text, c.start) === following && aloneOnLine(text, c));
    if (!candidate) {
      return false;
    }
    if (suppression.test(text.slice(candidate.start, candidate.end).trim())) {
      return true;
    }
    next = candidate;
  }
}

function expand(text, start, end) {
  while (end > start && (text[end - 1] === '\n' || text[end - 1] === '\r')) {
    end--;
  }
  const first = lineStart(text, start);
  const last = lineEnd(text, end);
  const before = text.slice(first, start);
  const after = text.slice(end, last);
  if (before.trim() === '' && after.trim() === '') {
    return { start: first, end: Math.min(last + 1, text.length), whole: true };
  }
  if (after.trim() === '') {
    let trimmed = start;
    while (trimmed > first && (text[trimmed - 1] === ' ' || text[trimmed - 1] === '\t')) {
      trimmed--;
    }
    return { start: trimmed, end: last, whole: false };
  }
  let following = end;
  const spaceBefore = start === first || /[\s([]/.test(text[start - 1]);
  while (spaceBefore && following < last && (text[following] === ' ' || text[following] === '\t')) {
    following++;
  }
  return { start, end: following, whole: false };
}

function tidy(text, junction) {
  const lines = text.split('\n');
  let index = text.slice(0, junction).split('\n').length - 1;
  const blank = (i) => i >= 0 && i < lines.length && lines[i].trim() === '';
  const trimmed = (i) => (i >= 0 && i < lines.length ? lines[i].trim() : '');
  if (index === 0 || /[{([>]$/.test(trimmed(index - 1))) {
    while (blank(index) && index < lines.length - 1) {
      lines.splice(index, 1);
    }
  }
  if (/^([})\]]|<\/)/.test(trimmed(index))) {
    while (blank(index - 1) && index - 1 > 0) {
      lines.splice(index - 1, 1);
      index--;
    }
  }
  if (blank(index - 1) && blank(index) && index < lines.length - 1) {
    lines.splice(index, 1);
  }
  return lines.join('\n');
}

function remove(text, comments) {
  const spans = comments.map((c) => expand(text, c.start, c.end)).sort((a, b) => a.start - b.start);
  const merged = [];
  for (const span of spans) {
    const last = merged.at(-1);
    if (last && span.start <= last.end) {
      last.end = Math.max(last.end, span.end);
      last.whole = last.whole && span.whole;
    } else {
      merged.push({ ...span });
    }
  }
  let output = '';
  let position = 0;
  const junctions = [];
  for (const span of merged) {
    output += text.slice(position, span.start);
    if (span.whole) {
      junctions.push(output.length);
    }
    position = span.end;
  }
  output += text.slice(position);
  for (const junction of junctions.reverse()) {
    output = tidy(output, junction);
  }
  return output;
}

const findings = [];
let checked = 0;
for (const path of files()) {
  const kind = classify(path);
  if (!kind) {
    continue;
  }
  const full = join(root, path);
  const text = readFileSync(full, 'utf8');
  const comments =
    kind === 'script'
      ? scriptComments(path, text)
      : kind === 'html'
        ? htmlComments(text)
        : styleComments(text, 0, text.length, kind === 'scss');
  checked++;
  const offending = comments.filter((c) => !allowed(text, c, comments));
  for (const comment of offending) {
    findings.push({ path, line: lineOf(text, comment.start), excerpt: text.slice(comment.start, comment.end).replace(/\s+/g, ' ').slice(0, 80) });
  }
  if (fix && offending.length > 0) {
    writeFileSync(full, remove(text, offending));
  }
}

for (const finding of findings) {
  console.log(`${finding.path}:${finding.line}: comment: ${finding.excerpt}`);
}
if (findings.length === 0) {
  console.log(`Comments: none outside the exceptions in ${checked} files (TypeScript, JavaScript, HTML, SCSS, CSS).`);
} else if (fix) {
  console.log(`Removed ${findings.length} comments in ${new Set(findings.map((f) => f.path)).size} files. Run the linter and review the diff.`);
} else {
  console.log(`${findings.length} comments outside the exceptions (CONTRIBUTING.md, "Comments"). Remove them, or run with --fix.`);
  process.exitCode = 1;
}
