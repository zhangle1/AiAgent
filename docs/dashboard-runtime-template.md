# React ECharts dashboard runtime

The preloaded template is stored in `AiAgent/dashboard-templates/react-echarts-operations`.
It uses React, Vite, and ECharts. Its data flow deliberately follows a small subset of the
dataset-centric approach used by Apache Superset: a dataset supplies named fields, metrics
are declared separately, and chart options consume the dataset rather than embedding data
access inside visual components.

Creating an application with this template copies the source into
`backed/data/dashboard-workspaces/{application-id}`. The managed runtime only accepts a
template workspace whose `package.json` retains the exact `"dev": "vite"` script. It runs:

```text
npm run dev -- --host 0.0.0.0 --port <dynamically allocated 4310..4399> --strictPort
```

The process output is retained in a bounded server-side log and exposed by
`GET /api/v1/dashboard-applications/{id}/runtime`. The workbench polls that status and
renders the running port, errors, and log output in its bottom terminal.

When `node_modules/.bin/vite` is absent, the server performs
`npm ci --ignore-scripts --no-audit --no-fund` from the copied template lockfile, then starts Vite.
Lifecycle scripts remain disabled during installation, so an edited project cannot turn dependency
installation into an arbitrary server command.
