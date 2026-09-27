import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { defineConfig, globalIgnores } from 'eslint/config'

export default defineConfig([
  globalIgnores(['dist']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat.recommended,
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      ecmaVersion: 2020,
      globals: globals.browser,
    },
  },
  // Keep src/api/** and src/types/** portable: they may not depend on the
  // React renderer. See docs/adr/0002-client-platform-strategy.md — preserves
  // the option to extract a @vora/api-types package later, and keeps the
  // import graph from accidentally tangling presentational code into the
  // services that native clients also need to mirror.
  {
    files: ['src/api/**/*.{ts,tsx}', 'src/types/**/*.{ts,tsx}'],
    rules: {
      // The TS-ESLint variant supports allowTypeImports; the core rule does not.
      // Keep the core rule off in this scope and rely on the TS-ESLint one.
      'no-restricted-imports': 'off',
      '@typescript-eslint/no-restricted-imports': ['error', {
        patterns: [
          {
            // theme/types is pure type definitions with no React/runtime deps and is
            // legitimately part of the API payload shape (themeService, clientTemplateService).
            // Allowed as type-only via the second rule below; runtime import still blocked.
            group: [
              '**/components/**',
              '**/pages/**',
              '**/layouts/**',
              '**/dialogs/**',
              '**/contexts/**',
              '**/theme/**',
            ],
            allowTypeImports: false,
            message: 'src/api and src/types must stay renderer-agnostic. Type-only imports from theme/types are allowed; everything else under theme/ is runtime code. See docs/adr/0002-client-platform-strategy.md.',
          },
          {
            group: ['**/theme/types'],
            allowTypeImports: true,
            message: 'theme/types is allowed as a type-only import (import type { ... } from "../../theme/types").',
          },
          {
            group: ['react', 'react-dom', 'react-router-dom'],
            message: 'src/api and src/types must not import React. They run in tests and may be lifted into shared tooling.',
          },
        ],
      }],
    },
  },
])
