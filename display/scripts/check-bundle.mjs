// Controlli sul bundle di produzione (dist/), da eseguire dopo `npm run build`.
// 1. Nessun indirizzo esterno: il monitor deve partire identico col router staccato (TASKS M0).
// 2. Ogni @font-face punta a un file presente in dist/.
// 3. Nessun alert/confirm/prompt nel codice (CLAUDE.md).
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

// fileURLToPath e non URL.pathname: su Windows (e con spazi o accenti nel percorso) pathname non è un percorso.
const dist = fileURLToPath(new URL('../dist/', import.meta.url));
const problems = [];

const files = [];
const walk = (dir) => {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) walk(path);
    else files.push(path);
  }
};
if (!existsSync(dist)) {
  console.error('dist/ non esiste: esegui prima npm run build');
  process.exit(1);
}
walk(dist);

// I namespace XML (SVG, MathML, xlink…) sono identificatori, non richieste di rete.
const allowed = ['http://www.w3.org/'];
for (const file of files.filter((f) => /\.(html|css|js)$/.test(f))) {
  const text = readFileSync(file, 'utf8');
  for (const match of text.matchAll(/(?:https?:)?\/\/[a-z0-9.-]+\.[a-z]{2,}[^\s"'`)]*/gi)) {
    const url = match[0];
    if (!/^https?:/i.test(url) && !url.startsWith('//')) continue;
    if (allowed.some((ok) => url.startsWith(ok))) continue;
    // I commenti di licenza di React citano facebook.com: non sono richieste.
    if (/react\.dev|reactjs\.org|facebook\.com/.test(url)) continue;
    problems.push(`${relative(dist, file)}: indirizzo esterno ${url}`);
  }
  if (file.endsWith('.css')) {
    for (const match of text.matchAll(/url\((['"]?)([^'")]+)\1\)/g)) {
      const target = match[2];
      if (target.startsWith('data:')) continue;
      if (!existsSync(join(dist, target.replace(/^\//, '')))) {
        problems.push(`${relative(dist, file)}: risorsa mancante ${target}`);
      }
    }
  }
  if (file.endsWith('.js') && /\b(window\.)?(alert|confirm|prompt)\s*\(/.test(text)) {
    problems.push(`${relative(dist, file)}: alert/confirm/prompt nel bundle`);
  }
}

if (problems.length > 0) {
  console.error(problems.join('\n'));
  process.exit(1);
}
console.log(`Bundle a posto: ${files.length} file, nessuna richiesta esterna, font presenti.`);
