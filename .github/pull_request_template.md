## What happens

<!-- The problem or the missing feature, as a player or developer sees it. Steps to reproduce for a bug. -->

## Cause

<!-- For a bug: why it happens (which game or mod code, which condition). Leave out for a new feature. -->

## Change

<!-- What the pull request changes and why this way. Name the files and the game methods that are patched. -->

## Testing

<!-- How it was tested: system (Windows or Linux), game build (Castle Story 5.6 or the Unity 6 port), mod version,
     what you did in game, what happened before and after. Screenshots or log lines are welcome. -->

## AI use

<!-- Required. Tick one and fill in the rest. -->

- [ ] No AI was used.
- [ ] AI was used.
  - Tool and model (for example Claude Code with Claude Opus, GitHub Copilot, ChatGPT with GPT-5):
  - What it did (for example wrote the code, found the cause, wrote the description, reviewed):

## Checklist

- [ ] `CHANGELOG.md` has an entry at the top under `## Unreleased` (English, one tag: `[ADD]`, `[CHANGE]`, `[FIX]`
      or `[REMOVE]`).
- [ ] A new feature is listed in `docs/Features.md` and `Core/Features.cs`.
- [ ] C# style: `tools/check-style.sh` passes (braces on every block, 24-hour time, day-first dates), and numbers
      are right-aligned in the UI.
- [ ] Saves made before this change still load, and saves made with it load without the mod.
- [ ] Nothing of the original game (code, Lua, assets, data) is added to the repository.
- [ ] The pull request does one thing; unrelated changes go in their own pull request.
- [ ] I agree that this contribution is licensed under the GNU GPL v3 or later with the additional permission in
      `NOTICE`, as the rest of the code.
