# GitHub Pages Deployment

The GitHub Pages workflow publishes a static website from the `landing` folder and copies `docs` into the published artifact.

Workflow file:

```text
.github/workflows/pages.yml
```

Published content:

```text
landing/        → website root
docs/           → website documentation files
```

The workflow runs on pushes to `main` that change `landing`, `docs`, or the workflow itself. It can also be started manually from the Actions tab.
