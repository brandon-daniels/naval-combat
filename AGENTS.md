# Naval Combat development guidance

## Read first

- Read `docs/PROJECT_CONTEXT.md` for the current project baseline and verified references.
- This is a modern S&box C# game built around Scenes, GameObjects, and Components.
- Check the installed engine APIs and current official documentation before using unfamiliar APIs. Avoid legacy Entity/GameManager tutorials unless explicitly supported by this project.

## Project layout

- `Code/`: runtime C# and, when needed, Razor UI.
- `Editor/`: editor-only tooling; keep editor dependencies out of runtime code.
- `Assets/`: source scenes, prefabs, models, materials, and other resources.
- `ProjectSettings/`: input, collision, and platform configuration.
- `naval_combat.sbproj`: project manifest; startup scene is currently `scenes/minimal.scene`.

## Implementation conventions

- Follow `.editorconfig`: tabs, Allman braces, and spaces inside non-empty method parentheses.
- Prefer focused components with inspector-exposed configuration where appropriate.
- Use the existing input action names in `ProjectSettings/Input.config`; add or rename bindings deliberately.
- Treat multiplayer ownership and authority as explicit design decisions. Validate gameplay-changing requests at the authoritative peer; keep local input and presentation separate from shared state.
- Verify lifecycle, networking, physics, and UI API signatures against the installed version before implementation.
- Preserve serialized GUIDs, asset references, and scene settings when editing existing assets. Never edit compiled asset outputs in place of their sources.
- The editor generates `.csproj` and `.slnx` files; do not treat changes to those files as durable project configuration.
- Do not add unrelated gameplay scaffolding, packages, or frameworks to satisfy a small task.

## Validation

- Open `naval_combat.sbproj` in S&box and check compilation/hotload errors after code changes.
- Play the affected scene and verify the requested behavior. Compilation alone does not validate scene setup, engine API restrictions, assets, or networking.
- For multiplayer changes, test host and another client through the editor's network menu, including relevant ownership and reconnect cases.
- Command-line builds require a compatible .NET SDK and the installed S&box references. Generated project outputs point into the engine installation; inspect output paths before running a build.
- State what was actually validated and any unavailable checks. Update project context when architecture or setup changes materially.
