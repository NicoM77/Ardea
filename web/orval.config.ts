import { defineConfig } from 'orval';

/**
 * The API client is generated from the committed OpenAPI document (master plan 2.4, 10.1):
 * `npm run generate:api`. Reads become `httpResource` helpers, writes `HttpClient` services.
 * The output is committed; CI regenerates it and fails when it differs, which is the check that
 * client and document match. Never edit the generated files by hand.
 */
export default defineConfig({
  ardea: {
    input: { target: '../openapi/ardea.v1.json' },
    output: {
      mode: 'tags-split',
      target: 'projects/ardea/src/app/core/api/generated/endpoints',
      schemas: 'projects/ardea/src/app/core/api/generated/model',
      client: 'angular',
      clean: true,
      prettier: false,
      override: {
        angular: {
          retrievalClient: 'httpResource',
          observeOverloads: false,
        },
      },
    },
  },
});
