#!/usr/bin/env node
// Fails on high/critical npm advisories, except time-boxed exceptions in audit-allowlist.json.
//
//   node scripts/audit-check.mjs                  # run `npm audit` and compare with the allowlist
//   node scripts/audit-check.mjs --bundle .output # additionally: allowlisted packages must not be in the server bundle
//
// No dependencies on purpose (supply chain). An exception needs a reason and an expiry date; after that date the
// check fails again so the exception is re-reviewed.
import { execSync } from 'node:child_process'
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const BLOCKING = new Set(['high', 'critical'])
const rootUrl = new URL('..', import.meta.url)
const root = fileURLToPath(rootUrl)
const allowlist = JSON.parse(readFileSync(new URL('audit-allowlist.json', rootUrl), 'utf8'))
const today = new Date().toISOString().slice(0, 10)

function runAudit() {
  try {
    // Fixed command line, no user input: running it through the shell is safe and works for npm.cmd on Windows.
    return execSync('npm audit --json', { cwd: root, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 })
  }
  catch (error) {
    // npm audit exits with 1 when it finds anything; the JSON is still on stdout.
    if (error.stdout) return error.stdout
    throw error
  }
}

const report = JSON.parse(runAudit())
const advisories = new Map()
for (const vulnerability of Object.values(report.vulnerabilities ?? {})) {
  for (const via of vulnerability.via) {
    if (typeof via === 'object' && BLOCKING.has(via.severity)) {
      const id = via.url.split('/').pop()
      advisories.set(id, { id, package: via.name, severity: via.severity, title: via.title, url: via.url })
    }
  }
}

const problems = []
for (const advisory of advisories.values()) {
  const exception = allowlist.find(entry => entry.id === advisory.id)
  if (!exception) {
    problems.push(`${advisory.severity.toUpperCase()} ${advisory.package}: ${advisory.title} (${advisory.url})`)
  }
  else if (exception.expires < today) {
    problems.push(`Allowlist entry ${exception.id} (${exception.package}) expired on ${exception.expires} — re-review it.`)
  }
  else {
    console.log(`allowed until ${exception.expires}: ${advisory.severity} ${advisory.package} ${advisory.id} — ${exception.reason}`)
  }
}

for (const entry of allowlist) {
  if (!advisories.has(entry.id)) {
    console.log(`note: allowlist entry ${entry.id} (${entry.package}) is no longer reported and can be removed.`)
  }
}

const bundleIndex = process.argv.indexOf('--bundle')
if (bundleIndex !== -1) {
  const modules = join(process.argv[bundleIndex + 1] ?? '.output', 'server', 'node_modules')
  const shipped = new Set()
  const walk = (dir) => {
    if (!existsSync(dir)) return
    for (const name of readdirSync(dir)) {
      const path = join(dir, name)
      if (!statSync(path).isDirectory()) continue
      if (name.startsWith('@')) walk(path)
      else shipped.add(name)
      if (existsSync(join(path, 'node_modules'))) walk(join(path, 'node_modules'))
    }
  }
  walk(modules)
  for (const entry of allowlist) {
    if (shipped.has(entry.package)) {
      problems.push(`Allowlisted ${entry.package} (${entry.id}) is part of the production server bundle — the exception does not hold.`)
    }
  }
  console.log(`bundle check: ${shipped.size} packages in ${modules}, none of them allowlisted.`)
}

if (problems.length > 0) {
  console.error(`\nnpm audit check failed:\n- ${problems.join('\n- ')}`)
  process.exit(1)
}
console.log('npm audit check passed (no unexplained high/critical advisories).')
