# Configuration

Repository-level build, SDK, editor, and ignore configuration is kept in the repository root so standard .NET, Git, and editor tooling discovers it automatically:

- `global.json`
- `Directory.Build.props`
- `.editorconfig`
- `.gitignore`

`config/AGENTS.md` remains the Qwen coding-agent entry point.

Do not put API keys or other secrets in committed configuration files.
