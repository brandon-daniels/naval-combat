# Collaborating on Naval Combat

## Get started

1. Install Git and S&box through Steam.
2. Run `git clone https://github.com/brandon-daniels/naval-combat.git` and open `naval_combat.sbproj` in S&box.
3. Allow the editor to generate its projects and compile resources.
4. Open `Assets/scenes/naval_prototype.scene` and press Play locally.

Read [AGENTS.md](AGENTS.md) and [the project context](docs/PROJECT_CONTEXT.md) before changing gameplay. Sailing supports local play; the separate combat arena supports multiplayer testing.

Configure your own Git author name and email before committing. Use repository-local settings if this project needs a different identity from your other work:

```sh
git config user.name "Your Name"
git config user.email "your-email@example.com"
```

## Make a change

Start from an up-to-date `main` branch with a clean working tree:

```sh
git switch main
git pull --ff-only
git switch -c feature/describe-your-change
```

Keep changes focused. Save authored scene changes outside Play mode. Coordinate before editing the same scene or binary asset; scene merges require care to preserve GUIDs and component references.

Before committing, inspect `git status` and `git diff`. Commit source code, source assets, asset `.meta` files, and project settings. Generated projects, local editor settings, caches, and compiled resources are ignored, except the existing project's compiled shader convention. Do not commit credentials or local environment files.

After reviewing and staging your intended files:

```sh
git diff --cached
git commit -m "Describe the change"
git push -u origin feature/describe-your-change
```

Open a pull request into `main`, describe the behavior change and validation performed, and ask another collaborator to review it. Repository administrators can enable required reviews and protect `main` on the hosting service.

## Validate changes

- Check compilation and hotload errors in S&box, then play the affected scene.
- For ship or station changes, run the relevant editor console commands: `naval_test_helm`, `naval_test_sails`, and `naval_test_cannons`.
- For multiplayer work, test a host and another client, including ownership and reconnect behavior.
- Record unavailable checks. A command-line build alone does not validate scenes or engine behavior.

Keep generated `.csproj` and `.slnx` files out of commits. Update the project context when architecture or setup changes materially.
