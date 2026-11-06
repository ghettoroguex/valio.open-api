import { withBase } from "./site-paths.mjs";

/** Keep authored Markdown and MDX links valid under a GitHub Pages project path. */
export default function remarkBaseLinks() {
  return function transform(tree) {
    function visit(node) {
      if (["link", "image", "definition"].includes(node.type))
        node.url = withBase(node.url);
      if (["mdxJsxFlowElement", "mdxJsxTextElement"].includes(node.type)) {
        for (const attribute of node.attributes || []) {
          if (
            ["href", "src"].includes(attribute.name) &&
            typeof attribute.value === "string"
          ) {
            attribute.value = withBase(attribute.value);
          }
        }
      }
      for (const child of node.children || []) visit(child);
    }
    visit(tree);
  };
}
