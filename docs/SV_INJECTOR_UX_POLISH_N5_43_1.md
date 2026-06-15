# SV Injector UX Polish N5.43.1

This polish pass focuses on the main WPF workspace visual quality without changing the core injector workflow.

## Changes

- Removed the custom blue top strip because the native Windows title bar already provides the application frame.
- Reworked ribbon buttons with a premium WPF `ControlTemplate`:
  - rounded tactile surface,
  - subtle shadow,
  - hover border feedback,
  - pressed shadow reduction,
  - large lucide-style outline icons with small captions.
- Removed noisy header cards and publisher text from the ribbon area.
- Added selected adapter visibility to the bottom status bar.
- Kept concise operational status in the bottom bar only.
- Improved grid/table readability:
  - centered cell content vertically,
  - added consistent left/right cell inset,
  - centered checkbox cells,
  - cleaner column header style,
  - softer alternating row background.
- Added modern scrollbar sizing and thumb styling.

## Design intent

The UI should feel closer to a professional test-set workstation: fast to scan, compact, tactile, and visually quieter. Engineering information stays available, but the ribbon is now command-focused instead of status-heavy.
