import { execFileSync } from 'node:child_process';

const types = ['feat', 'fix', 'perf', 'revert', 'docs', 'style', 'refactor', 'test', 'build', 'ci', 'chore'];
const conventional = new RegExp(`^(${types.join('|')})(\\([^()\\s][^()]*\\))?!?: \\S`);
const mergeSubject = /^Merge (pull request #\d+ from |branch '|remote-tracking branch ')/;
const gitRevert = /^(Revert|Reapply) ".+"$/;
const expected = `Expected "<type>(<optional scope>)!: <description>" with a type out of ${types.join(', ')}.`;

function report(text) {
  console.log(process.env.GITHUB_ACTIONS === 'true' ? `::error::${text}` : text);
}

function accepted(commit) {
  return commit.merge || mergeSubject.test(commit.subject) || conventional.test(commit.subject) || gitRevert.test(commit.subject);
}

function commits(base, head) {
  const output = execFileSync('git', ['log', '--format=%H%x09%P%x09%s', `${base}..${head}`, '--'], { encoding: 'utf8' });
  return output
    .split('\n')
    .filter(Boolean)
    .map((line) => {
      const [sha, parents, ...subject] = line.split('\t');
      return { sha, merge: parents.trim().split(' ').length > 1, subject: subject.join('\t') };
    });
}

function checkTitle(title) {
  if (conventional.test(title)) {
    console.log(`The pull request title "${title}" follows Conventional Commits.`);
    return;
  }
  report(`The pull request title "${title}" is not a Conventional Commit`);
  console.log(`
${expected}
The title becomes the subject of the merge commit, which is what the changelog lists. Edit the title.`);
  process.exit(1);
}

function checkRange(base, head) {
  const all = commits(base, head);
  const rejected = all.filter((commit) => !accepted(commit));
  for (const commit of rejected) {
    report(`${commit.sha.slice(0, 7)} "${commit.subject}" is not a Conventional Commit`);
  }
  if (rejected.length > 0) {
    console.log(`
${expected}
Merge commits and git's "Revert \\"…\\"" and "Reapply \\"…\\"" commits pass. Reword commits before you push them
(git rebase -i ${base}); a pushed branch is not rewritten, so carry its changes over to a new branch instead.`);
    process.exit(1);
  }
  console.log(`${all.length} commit(s) in ${base}..${head} follow Conventional Commits.`);
}

const args = process.argv.slice(2);
if (args[0] === '--title') {
  checkTitle(args[1] ?? '');
} else {
  checkRange(args[0] ?? 'origin/main', args[1] ?? 'HEAD');
}
