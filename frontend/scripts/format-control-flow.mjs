import fs from 'node:fs/promises'
import path from 'node:path'
import ts from 'typescript'

const check = process.argv.includes('--check')

async function formatDirectory(directory) {
  for (const entry of await fs.readdir(directory, { withFileTypes: true })) {
    if (['node_modules', 'dist', '.git'].includes(entry.name)) {
      continue
    }
    const file = path.join(directory, entry.name)
    if (entry.isDirectory()) {
      await formatDirectory(file)
    } else if (/\.(?:tsx?|m?js)$/.test(entry.name)) {
      const source = await fs.readFile(file, 'utf8')
      const tree = ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true)
      const edits = []
      function wrap(statement) {
        if (statement && !ts.isBlock(statement)) {
          edits.push({ position: statement.getStart(tree), text: '{\n' })
          edits.push({ position: statement.end, text: '\n}' })
        }
      }
      function visit(node) {
        if (ts.isIfStatement(node)) {
          wrap(node.thenStatement)
          if (node.elseStatement && !ts.isIfStatement(node.elseStatement)) {
            wrap(node.elseStatement)
          }
        } else if (
          ts.isForStatement(node) ||
          ts.isForOfStatement(node) ||
          ts.isForInStatement(node) ||
          ts.isWhileStatement(node) ||
          ts.isDoStatement(node) ||
          ts.isWithStatement(node)
        ) {
          wrap(node.statement)
        }
        ts.forEachChild(node, visit)
      }
      visit(tree)
      if (edits.length) {
        if (check) {
          console.error(`Missing control-flow braces: ${file}`)
          process.exitCode = 1
        } else {
          let formatted = source
          for (const edit of edits.sort((a, b) => b.position - a.position)) {
            formatted = formatted.slice(0, edit.position) + edit.text + formatted.slice(edit.position)
          }
          await fs.writeFile(file, formatted)
        }
      }
    }
  }
}

await formatDirectory('.')
