// GitHub project Pages needs a path prefix; custom domains usually use `/`.
// Accept the complete deployment URL so these settings cannot drift apart.
const deploymentUrl = new URL(
  process.env.DOCS_SITE_URL ||
    "https://valio-projects.github.io/valio.open-api/",
);

export const site = deploymentUrl.origin;
export const base = `${deploymentUrl.pathname.replace(/\/$/, "")}/`;

/** Prefix a site-local URL once, preserving external and protocol-relative URLs. */
export function withBase(url) {
  if (typeof url !== "string" || !url.startsWith("/") || url.startsWith("//"))
    return url;
  if (base === "/" || url === base.slice(0, -1) || url.startsWith(base))
    return url;
  return `${base}${url.slice(1)}`;
}
