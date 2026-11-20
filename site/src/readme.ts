import source from '../../README.md?raw';

const readme = source.replace(/\r\n/g, '\n');

function section(heading: string): string {
  const start = readme.indexOf(`\n## ${heading}\n`);
  if (start < 0) {
    throw new Error(`README.md has no section «${heading}»`);
  }
  const rest = readme.slice(start + heading.length + 5);
  const end = rest.search(/\n## /);
  return end < 0 ? rest : rest.slice(0, end);
}

function escape(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

export function inline(text: string): string {
  return escape(text)
    .replace(/\[([^\]]+)\]\([^)]+\)/g, '$1')
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
}

const blocks = readme.split(/\n{2,}/).map((block) => block.trim());
const prose = blocks.filter((block) => block !== '' && !block.startsWith('<') && !block.startsWith('#') && !block.startsWith('**'));

export const purpose = prose.slice(0, 2);

export const features = [...section('Features').matchAll(/^- \*\*(.+?)\*\* (.+)$/gm)].map((match) => ({
  title: match[1].replace(/\.$/, ''),
  text: match[2],
}));

const quickStart = section('Quick start');
const note = /^> (.+)$/m.exec(quickStart);
const commands = /```bash\n([\s\S]*?)\n```/.exec(quickStart);
if (!note || !commands || features.length === 0 || purpose.length < 2) {
  throw new Error('README.md no longer has the purpose, features or quick start the landing page reads');
}

export const quickStartNote = note[1];
export const quickStartCommands = commands[1];
