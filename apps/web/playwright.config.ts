import { defineConfig, devices } from '@playwright/test'
import { fileURLToPath } from 'node:url'
import path from 'node:path'
import process from 'node:process'

const webRoot = path.dirname(fileURLToPath(import.meta.url))
const artifacts = path.resolve(webRoot, '../../.artifacts/m1')

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 45_000,
  expect: { timeout: 8_000 },
  outputDir: path.join(artifacts, 'test-results'),
  reporter: [
    ['list'],
    ['html', { outputFolder: path.join(artifacts, 'playwright-report'), open: 'never' }],
    ['json', { outputFile: path.join(artifacts, 'playwright-results.json') }],
  ],
  use: {
    ...devices['Desktop Chrome'],
    channel: process.env.PLAYWRIGHT_CHANNEL || 'chrome',
    baseURL: 'http://127.0.0.1:5188',
    viewport: { width: 1440, height: 900 },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
})
