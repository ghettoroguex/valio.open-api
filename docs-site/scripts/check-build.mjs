import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import { join, resolve, sep } from "node:path";
import { base, site } from "./site-paths.mjs";

const output = resolve("dist");
const errors = [];
let pages = 0;

/** Check emitted HTML so hero, navigation, and asset URLs share the deployment path. */
function checkDirectory(directory) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const file = join(directory, entry.name);
    if (entry.isDirectory()) {
      checkDirectory(file);
      continue;
    }
    if (!entry.name.endsWith(".html")) continue;
    pages++;
    for (const match of readFileSync(file, "utf8").matchAll(
      /\b(?:href|src)="(\/[^"\s]*)"/g,
    )) {
      const link = match[1];
      if (link.startsWith("//")) continue;
      const pathname = decodeURIComponent(new URL(link, site).pathname);
      if (!pathname.startsWith(base)) {
        errors.push(`${file}: ${link} is outside deployment base ${base}`);
        continue;
      }
      const target = resolve(output, pathname.slice(base.length));
      if (target !== output && !target.startsWith(`${output}${sep}`)) {
        errors.push(`${file}: ${link} escapes the output directory`);
        continue;
      }
      if (
        !existsSync(target) ||
        (statSync(target).isDirectory() &&
          !existsSync(join(target, "index.html")))
      ) {
        errors.push(`${file}: ${link} has no built page or asset`);
      }
    }
  }
}

checkDirectory(output);
if (errors.length)
  throw new Error(`Invalid built URLs:\n${[...new Set(errors)].join("\n")}`);
console.log(
  `Validated deployment URLs in ${pages} HTML files at ${site}${base}`,
);
