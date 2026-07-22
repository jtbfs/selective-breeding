# What's this?

This is a mod for selective breeding in The Bibites! It can be used to selectively breed bibites (of your choice) with other bibites (also of your choice), or templates.

Which means this is extremely useful for minimizing evolution degradation, and keeping bibites on a similar evolutionary pathway as the selective bred (whether that is the template, other bibites, etc) while finetuning it (or making it better).

So it's really good for competition purposes, or in general.

# So, how do you... "selectively breed"?

Simply assign this custom tag to a bibite/template, and follow the format: `mix-{interval}-{brainTransferType}-{genePercent}-{brainPercent}-{customTargetTag}`
- **`interval`**
  - Every `{interval}` eggs, selectively breed the species with the tag.
    
- **`brainTransferType`**
  - There are two options:  "`t1`" and "`t2`"
   - "`t1`": simple - chance is based on `{brainPercent}`, and will replace the child's brain with the tag's brain entirely
   - "`t2`": realistic - `{brainPercent}` crossover similar to gene crossover

- **`genePercent`**
  - The percentage to cross-over genes.

- **`brainPercent`**
  - The percentage to cross-over brain (if `t2`), otherwise the chance to replace the brain.
   
- **`customTargetTag`**
  - The species' custom tag that will be used to selectively breed with the tag. 

For competition purposes, `t1` is recommended, otherwise for evolution, `t2`.
Also, try alternating between each to see how much finetuning you can make out of it.

All options are defaultable, and nullable (excluding `interval`, which is required). It should look like this: `mix-3`
- This will make it so every bibite's every 3rd egg will be selectively bred.

Default settings (if you didn't customize any options):
- `brainTransferType`: `t2`
- `genePercent`: randomized between `0%` to `100%`
- `brainPercent`: `50%`
- `customTargetTag`: none (all)

Example of the correct tag applicance: <img width="1281" height="490" alt="image" src="https://github.com/user-attachments/assets/e98905f5-87ef-4303-b36f-d51409047374" />

> ⚠️ Math-related functions, biology-related stuff are AI-assisted, I am so SORRY, as I absolutely do not know ANY maths, nor do I know what a "realistic crossover" is, without relying on AI/googling for what the hell an acceptable one is.
> So, because that's basically almost all of this code, it is. But it does work beautifully, and fulfill my goal (minimize evolution degradation, and also be able to finetune my own engineered bibites)~
> On that note, I did learn a lot from that: there's a thing called "NEAT" (NeuroEvolution of Augmenting Topologies), which is where The Bibites takes inspiration from, specifically the brains (and I believe, genes as well?).
> Also, the game already has support for gene-crossover, I just kinda copied that for the brain-crossover as well. Close enough!
