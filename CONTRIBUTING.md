<a id="top"></a>

# Contributing

## TLDR

Disclose every use of generative AI, identifying each provider, model, model family,
version, and variant. Commit messages are sufficient; source/test content needs no
AI disclosure comments or banners. Explicitly declare when none was used. Follow
the library's verification policy and contribute under MIT.

[↑ Top](#top)

## Table of contents

- [TLDR](#tldr)
- [AI assistance disclosure](#ai-assistance-disclosure)
- [Submission requirements](#submission-requirements)
- [License](#license)

[↑ Top](#top)

## AI assistance disclosure

All contributions must disclose **any and all generative AI assistance**. This
applies to code, documentation, tests/fixtures, designs, issue reports, reviews,
and contribution messages. Include planning, research, completion, generation,
editing, translation, debugging, and review, even when outputs were rewritten.
Include IDE assistants, agents/subagents, and every model used by a routed workflow.

For each provider/model used, supply:

| Required field | Details |
| --- | --- |
| Provider | Organization supplying the model. |
| Tool/product | Client, IDE assistant, agent, or service used. |
| Model | Full model name and exact model identifier when available. |
| Model family | For example, GPT-6. |
| Version number | The model version/release, for example 6.1. |
| Variant/build | For example, Sol; include a dated snapshot/build if exposed. |
| Assistance scope | Tasks assisted and affected code, docs, tests, or review. |
| Human review/validation | What the contributor checked and remaining gaps. |

When a tool does not expose a required identity detail, explicitly write
`Unknown / not exposed` in that field and explain the limitation, including any
automatic model selection. Never guess, omit the field, or present inferred
identity as verified. Maintainers can request clarification before acceptance.
Use the identity actually used, rather than a product's current default model.

If no generative AI was used for any part, state **AI assistance: None**.
Keep disclosures current when additional AI assistance is used. Summaries suffice;
do not publish private prompts, credentials, or evaluation transcripts.

For committed changes, **disclosure in commit messages is sufficient**. There is no
requirement to put AI disclosure in source files, test content, comments, or banners.
A pull request may reference those commits or repeat their disclosure; it need not
duplicate complete commit disclosures. Preserve the disclosure when squashing commits.
Issue/review contributions without commits include it in their own submission.
Incomplete or misleading disclosure must be corrected before acceptance.

[↑ Top](#top)

## Submission requirements

- Follow [AGENTS.md](AGENTS.md) and [verification guidance](verification/README.md).
  Describe behavior changes, validation performed, and unverified cases.
- Keep fixtures generic and independent of consuming applications. Commit reusable
  source/scripts; exclude binaries, packages, captures, reports, and evaluation evidence.
- Preserve CRLF, requested braces, documentation navigation, and detailed commit bodies.
- Review all submitted content yourself; AI assistance does not replace responsibility
  for correctness, attribution, licensing, or verification.

[↑ Top](#top)

## License

Contributions are submitted under this repository's [MIT license](LICENSE).
Ensure you have the right to contribute the material and retain required notices.
The disclosure policy is a contribution requirement; it does not modify MIT's terms.

[↑ Top](#top)
