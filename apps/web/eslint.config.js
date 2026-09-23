import js from '@eslint/js'
import tseslint from 'typescript-eslint'
import vue from 'eslint-plugin-vue'

export default tseslint.config(
  { ignores: ['dist/**', 'node_modules/**', 'playwright-report/**', 'test-results/**'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  ...vue.configs['flat/recommended'],
  {
    files: ['**/*.vue'],
    languageOptions: { parserOptions: { parser: tseslint.parser, extraFileExtensions: ['.vue'] } },
    rules: { 'vue/multi-word-component-names': 'off', 'vue/max-attributes-per-line': 'off', 'vue/html-self-closing': 'off', 'vue/singleline-html-element-content-newline': 'off', 'vue/multiline-html-element-content-newline': 'off', 'vue/html-closing-bracket-newline': 'off' },
  },
  { languageOptions: { globals: { document: 'readonly', window: 'readonly', URL: 'readonly', Blob: 'readonly', File: 'readonly', fetch: 'readonly', crypto: 'readonly', TextEncoder: 'readonly', HTMLElement: 'readonly', Event: 'readonly', KeyboardEvent: 'readonly', BeforeUnloadEvent: 'readonly', HTMLInputElement: 'readonly', console: 'readonly', setTimeout: 'readonly' } } },
)
