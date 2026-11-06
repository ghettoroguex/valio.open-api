#!/usr/bin/env node
// -----------------------------------------------------------------------------
// summarize-trx.mjs — renders VSTest .trx results into the job summary.
//
// `dotnet test` writes one .trx per test project. On a red build the console log
// is long and the failure is easy to miss, so this collapses every result file
// into a single table plus a list of failing tests at the top of the job summary
// page. Parsing is done with regular expressions rather than an XML dependency:
// the two elements we need (`Counters` and failing `UnitTestResult`) have a
// stable, flat shape, and CI must not need an npm install to report on tests.
//
// Never exits non-zero — a broken summary must not turn a green build red. The
// authoritative pass/fail signal is `dotnet test`'s own exit code.
// -----------------------------------------------------------------------------

import { appendFileSync, readdirSync, readFileSync } from "node:fs";
import { join, relative } from "node:path";

/** Directories that never contain test output and are expensive to walk. */
const SKIP_DIRS = new Set(["node_modules", ".git", "obj", "bin", ".nuget", "dist"]);

/** Maximum number of failing tests listed individually before truncating. */
const MAX_LISTED_FAILURES = 50;

/**
 * Recursively collects every `.trx` file below a directory.
 *
 * @param {string} dir Directory to walk.
 * @param {string[]} [found] Accumulator used across recursive calls.
 * @returns {string[]} Absolute paths of all discovered .trx files.
 */
function findTrxFiles(dir, found = []) {
  let entries;
  try {
    entries = readdirSync(dir, { withFileTypes: true });
  } catch {
    return found;
  }

  for (const entry of entries) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      if (SKIP_DIRS.has(entry.name)) continue;
      findTrxFiles(full, found);
    } else if (entry.isFile() && entry.name.endsWith(".trx")) {
      found.push(full);
    }
  }
  return found;
}

/**
 * Extracts an XML attribute value from a tag string.
 *
 * @param {string} source The tag text to search.
 * @param {string} name Attribute name.
 * @returns {number} The parsed integer, or 0 when absent or unparsable.
 */
function attr(source, name) {
  const match = new RegExp(`${name}="(\\d+)"`).exec(source);
  return match ? Number.parseInt(match[1], 10) : 0;
}

/**
 * Parses one .trx file into aggregate counters and the names of failing tests.
 *
 * @param {string} path Absolute path to the .trx file.
 * @returns {{total: number, passed: number, failed: number, skipped: number,
 *   failures: string[]} | null} Parsed results, or null when the file cannot be
 *   read or contains no counters element.
 */
function parseTrx(path) {
  let xml;
  try {
    xml = readFileSync(path, "utf8");
  } catch {
    return null;
  }

  const counters = /<Counters\b[^>]*\/?>/.exec(xml);
  if (!counters) return null;
  const tag = counters[0];

  const failures = [];
  let skippedResults = 0;
  const resultRe = /<UnitTestResult\b[^>]*>/g;
  let match;
  while ((match = resultRe.exec(xml)) !== null) {
    // NUnit's adapter emits NotExecuted results while leaving the corresponding
    // aggregate counter at zero. Count the actual outcomes as well.
    if (/outcome="(?:NotExecuted|Inconclusive)"/.test(match[0])) skippedResults += 1;
    if (!/outcome="Failed"/.test(match[0])) continue;
    const name = /testName="([^"]*)"/.exec(match[0]);
    if (name) failures.push(name[1]);
  }

  return {
    total: attr(tag, "total"),
    passed: attr(tag, "passed"),
    failed: attr(tag, "failed"),
    // Some TRX producers supply counters without individual result elements.
    // Taking the larger count supports both representations without double-counting.
    skipped: Math.max(skippedResults, attr(tag, "notExecuted") + attr(tag, "inconclusive")),
    failures,
  };
}

/**
 * Derives a readable suite name from a .trx path by taking the test project
 * directory, which is the segment immediately above `TestResults`.
 *
 * @param {string} path Absolute path to the .trx file.
 * @param {string} root Repository root, used to relativise the fallback.
 * @returns {string} A short suite label.
 */
function suiteName(path, root) {
  const rel = relative(root, path).replace(/\\/g, "/");
  const segments = rel.split("/");
  const idx = segments.indexOf("TestResults");
  return idx > 0 ? segments[idx - 1] : rel;
}

/**
 * Escapes the pipe character so test names cannot break the markdown table.
 *
 * @param {string} text Arbitrary text.
 * @returns {string} Table-safe text.
 */
function cell(text) {
  return text.replace(/\|/g, "\\|");
}

/**
 * Entry point: discovers result files, aggregates them, and appends a markdown
 * report to `$GITHUB_STEP_SUMMARY` (falling back to stdout locally).
 *
 * @returns {void}
 */
function main() {
  const root = process.cwd();
  const files = findTrxFiles(root).sort();

  if (files.length === 0) {
    console.log("No .trx files found — nothing to summarise.");
    return;
  }

  const rows = [];
  const allFailures = [];
  let total = 0;
  let passed = 0;
  let failed = 0;
  let skipped = 0;

  for (const file of files) {
    const parsed = parseTrx(file);
    if (!parsed) continue;

    total += parsed.total;
    passed += parsed.passed;
    failed += parsed.failed;
    skipped += parsed.skipped;

    const name = suiteName(file, root);
    for (const failure of parsed.failures) allFailures.push(`${name} › ${failure}`);

    const icon = parsed.failed > 0 ? "❌" : "✅";
    rows.push(
      `| ${icon} ${cell(name)} | ${parsed.total} | ${parsed.passed} | ${parsed.failed} | ${parsed.skipped} |`,
    );
  }

  const heading = failed > 0 ? `❌ ${failed} failing` : `✅ ${passed} passing`;
  const lines = [
    `### Tests — ${heading}`,
    "",
    `**${total}** total · **${passed}** passed · **${failed}** failed · **${skipped}** skipped`,
    "",
  ];

  if (allFailures.length > 0) {
    lines.push("#### Failing tests", "");
    for (const failure of allFailures.slice(0, MAX_LISTED_FAILURES)) {
      lines.push(`- \`${failure}\``);
    }
    if (allFailures.length > MAX_LISTED_FAILURES) {
      lines.push(`- …and ${allFailures.length - MAX_LISTED_FAILURES} more.`);
    }
    lines.push("");
  }

  lines.push(
    "<details><summary>Per-suite breakdown</summary>",
    "",
    "| Suite | Total | Passed | Failed | Skipped |",
    "| --- | ---: | ---: | ---: | ---: |",
    ...rows,
    "",
    "</details>",
    "",
  );

  const report = lines.join("\n");
  console.log(report);

  if (process.env.GITHUB_STEP_SUMMARY) {
    appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${report}\n`, "utf8");
  }
}

try {
  main();
} catch (err) {
  // A summary is a convenience, never a gate.
  console.error(`summarize-trx: ${err instanceof Error ? err.message : String(err)}`);
}
