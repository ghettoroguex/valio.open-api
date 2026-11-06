// @ts-check
import { defineConfig } from "astro/config";
import starlight from "@astrojs/starlight";
import starlightImageZoom from "starlight-image-zoom";
import starlightLinksValidator from "starlight-links-validator";
import { site, base } from "./scripts/site-paths.mjs";
import remarkBaseLinks from "./scripts/remark-base-links.mjs";

// https://astro.build/config
export default defineConfig({
  site,
  base,
  server: { host: "0.0.0.0" },
  markdown: { remarkPlugins: [remarkBaseLinks] },

  // English content lives at the site root. Preserve the English home alias;
  // dynamic redirects cannot resolve static routes under a project base path.
  redirects: {
    "/en": base,
  },

  integrations: [
    starlight({
      title: "ValIO Docs",
      description:
        "Type-safe HTTP clients, models and schemas for 12 languages from OpenAPI 3.x.",

      // Social links (Starlight ≥0.33 expects array form).
      social: [
        {
          icon: "github",
          label: "GitHub",
          href: "https://github.com/valio-projects/valio.open-api",
        },
      ],

      // "Edit this page" links point at the main branch in the docs-site folder.
      editLink: {
        baseUrl:
          "https://github.com/valio-projects/valio.open-api/edit/main/docs-site/",
      },

      lastUpdated: true,

      // Four locales. English uses Starlight's special `root` key — its files
      // live at /docs root (no /en/ prefix); other locales serve under their key.
      defaultLocale: "root",
      locales: {
        root: { label: "English", lang: "en" },
        ru: { label: "Русский", lang: "ru" },
        de: { label: "Deutsch", lang: "de" },
        "zh-cn": { label: "简体中文", lang: "zh-CN" },
      },

      // Code blocks: GitHub themes + wrap long lines by default.
      expressiveCode: {
        themes: ["github-dark", "github-light"],
        defaultProps: {
          wrap: true,
        },
      },

      // Explicit sidebar groups. Reference/CLI/Languages/Configuration default
      // to collapsed so the landing TOC stays scannable.
      sidebar: [
        {
          label: "Getting Started",
          items: [
            { label: "Introduction", link: "/" },
            { label: "Getting started", link: "/getting-started/" },
            { label: "FAQ", link: "/faq/" },
          ],
        },
        {
          label: "Frontends",
          autogenerate: { directory: "frontends" },
        },
        {
          label: "CLI Reference",
          autogenerate: { directory: "cli" },
          collapsed: true,
        },
        {
          label: "Web App",
          autogenerate: { directory: "web" },
        },
        {
          label: "Desktop App",
          autogenerate: { directory: "desktop" },
        },
        {
          label: "Languages",
          autogenerate: { directory: "languages" },
          collapsed: true,
        },
        {
          label: "Stacks",
          autogenerate: { directory: "stacks" },
          collapsed: true,
        },
        {
          label: "Configuration",
          autogenerate: { directory: "configuration" },
          collapsed: true,
        },
        {
          label: "Architecture",
          autogenerate: { directory: "architecture" },
        },
        {
          label: "Recipes",
          autogenerate: { directory: "recipes" },
        },
        {
          label: "Reference",
          autogenerate: { directory: "reference" },
          collapsed: true,
        },
        {
          label: "Contributing",
          autogenerate: { directory: "contributing" },
        },
      ],

      // Plugins:
      //  - starlightLinksValidator: fails the build on broken internal links.
      //    errorOnFallbackPages=false: don't fail when a translated page is
      //    missing and falls back to English.
      //  - starlightImageZoom: click any image to enlarge.
      plugins: [
        starlightLinksValidator({ errorOnFallbackPages: false }),
        starlightImageZoom(),
      ],
    }),
  ],
});
