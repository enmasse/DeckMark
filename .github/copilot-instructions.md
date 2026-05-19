# Copilot Instructions

## Project Guidelines
- Use single-keystroke terminal controls for viewer interaction instead of full commands.
- Use non-deterministic random behavior for viewer transition interpretations rather than deterministic randomness.

### Presentation Mode
- When available, select a monitor that is not the main (primary) monitor for audience presentations; do not use the main monitor as the presentation target.
- If displays are mirrored, attempt to break mirroring to create a distinct non-primary presentation target instead of refusing when no separate monitor is detected.
- For DeckMark presentation mode, prefer the borderless-window approach over fullscreen when placing the audience window on the presentation display.

## Testing
- Use self-contained test assets or generated input when tests depend on deck content; avoid relying on mutable presentation files in the repository