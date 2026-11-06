import { defineCollection } from "astro:content";
import { docsLoader } from "@astrojs/starlight/loaders";
import { docsSchema } from "@astrojs/starlight/schema";
import { withBase } from "../scripts/site-paths.mjs";

export const collections = {
  docs: defineCollection({
    loader: docsLoader(),
    schema: (context) =>
      docsSchema()(context).transform((data) => {
        for (const action of data.hero?.actions || [])
          action.link = withBase(action.link);
        return data;
      }),
  }),
};
