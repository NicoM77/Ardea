import { execFileSync } from 'node:child_process';

const types = ['feat', 'fix', 'perf', 'revert', 'docs', 'style', 'refactor', 'test', 'build', 'ci', 'chore'];
const conventional = new RegExp(`^(${types.join('|')})(\\([^()\\s][^()]*\\))?!?: \\S`);
const mergeSubject = /^Merge (pull request #\d+ from |branch '|remote-tracking branch ')/;
const githubRevert = /^Revert ".+"$/;

function accepted(commit) {
  return commit.merge || mergeSubject.test(commit.subject) || conventional.test(commit.subject) || githubRevert.test(commit.subject);
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

const [base = 'origin/main', head = 'HEAD'] = process.argv.slice(2);
const all = commits(base, head);
const rejected = all.filter((commit) => !accepted(commit));

for (const commit of rejected) {
  const text = `${commit.sha.slice(0, 7)} "${commit.subject}" is not a Conventional Commit`;
  console.log(process.env.GITHUB_ACTIONS === 'true' ? `::error::${text}` : text);
}

if (rejected.length > 0) {
  console.log(`
Expected "<type>(<optional scope>)!: <description>" with a type out of ${types.join(', ')}.
Merge commits and GitHub's "Revert \\"…\\"" commits pass. Reword the others (git rebase -i ${base}) and push with --force-with-lease.`);
  process.exit(1);
}

console.log(`${all.length} commit(s) in ${base}..${head} follow Conventional Commits.`);
