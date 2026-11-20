import starlight from '@astrojs/starlight';
import { defineConfig, passthroughImageService } from 'astro/config';
import starlightOpenAPI, { openAPISidebarGroups } from 'starlight-openapi';
import { base, repository, site } from './site.config.mjs';

const focusableTables = {
  name: 'ardea-focusable-tables',
  hooks: {
    'astro:config:setup': ({ config }) => {
      config.markdown.processor.options.hastPlugins.push({
        name: 'ardea-focusable-tables',
        element: { filter: ['table'], visit: (node, ctx) => ctx.setProperty(node, 'tabIndex', 0) },
      });
    },
  },
};

export default defineConfig({
  site,
  base,
  trailingSlash: 'always',
  image: { service: passthroughImageService() },
  vite: { server: { fs: { allow: ['..'] } } },
  integrations: [
    focusableTables,
    starlight({
      title: 'Ardea',
      description: 'Self-hosted, open-source early warning for security vulnerabilities.',
      logo: { src: '../docs/design/brand/ardea-logo.svg', alt: 'Ardea', replacesTitle: true },
      favicon: '/media/ardea-favicon.svg',
      social: [{ icon: 'github', label: 'Source code on GitHub', href: repository }],
      customCss: ['../web/projects/ui/src/styles/tokens.css', './src/styles/fonts.css', './src/styles/docs.css'],
      components: {
        ThemeProvider: './src/components/ThemeProvider.astro',
        ThemeSelect: './src/components/ThemeSelect.astro',
      },
      expressiveCode: { themes: ['github-light'], useStarlightDarkModeSwitch: false },
      plugins: [
        starlightOpenAPI([
          {
            base: 'api',
            schema: '../openapi/ardea.v1.json',
            sidebar: { label: 'API reference', operations: { badges: true } },
          },
        ]),
      ],
      sidebar: [
        { label: 'Home', link: '/' },
        {
          label: 'Run Ardea',
          items: [
            { label: 'Overview', slug: 'operations' },
            { label: 'Try it', slug: 'operations/try-it' },
            { label: 'Installation', slug: 'operations/installation' },
            { label: 'Configuration', slug: 'operations/configuration' },
            { label: 'Operation', slug: 'operations/operation' },
            { label: 'Update', slug: 'operations/update' },
            { label: 'Add a source', slug: 'operations/add-a-source' },
          ],
        },
        {
          label: 'Architecture',
          items: [
            { label: 'Overview', slug: 'architecture' },
            { label: 'Decision records', slug: 'adr' },
            { label: 'All decisions', collapsed: true, items: [{ autogenerate: { directory: 'adr' } }] },
          ],
        },
        ...openAPISidebarGroups,
        {
          label: 'Security',
          items: [
            { label: 'Security policy', slug: 'security' },
            { label: 'Threat review', slug: 'security/threat-review' },
          ],
        },
        { label: 'Contributing', slug: 'contributing' },
      ],
    }),
  ],
});
