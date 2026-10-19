<a id="top"></a>

# Contributing

## TLDR

Disclose all generative AI assistance with provider, tool/product, and model/version.
Commit messages are sufficient; source/test content needs no disclosure banners.
Follow the library's verification policy and contribute under MIT.

[↑ Top](#top)

## Table of contents

- [TLDR](#tldr)
- [AI assistance disclosure](#ai-assistance-disclosure)
- [Submission requirements](#submission-requirements)
- [License](#license)

[↑ Top](#top)

## AI assistance disclosure

All contributions must disclose **any and all generative AI assistance**, including
planning, code, documentation, tests, reviews, and work through agents/subagents.
For each provider/model used, this format is sufficient:

```yaml
AI assistance disclosure:
Provider: OpenAI
Tool/product: Codex
Model/version: GPT-6.1 Sol
```

These three fields are sufficient. Give the model/version actually used; if it is
unavailable, state `Unknown / not exposed`. If no generative AI was used, state
**AI assistance: None**.

Put the disclosure in commit messages. PRs may reference those commits; source/test
comments or banners are not required. Preserve disclosures when squashing. Issues
and reviews without commits carry their disclosure in the submission.

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
