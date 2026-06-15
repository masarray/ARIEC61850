# SV Injector UX Polish N5.43.2

This patch refines the SV Injector workspace shell with a more premium, compact, and readable WPF UI.

## UX changes

- Ribbon buttons now use equal compact square sizing.
- Long button labels were shortened for fast recognition, including `Start Injection` to `Start`.
- Ribbon button chrome uses rounded corners, subtle elevation, hover depth, and pressed/tactile scaling.
- Header noise was reduced by removing the active workspace pill from the ribbon area.
- Selected adapter, stream, and run status remain available in the bottom status bar.
- Workspace cards now use soft elevation and blue gradient headers.
- Manual, Ramp, and Sequencer workspace columns now use proportional sizing with minimum widths for better responsiveness.
- DataGrid selected cells use harmonized design tokens so selected text remains readable on light blue selection backgrounds.
- DataGrid cell content uses centered vertical alignment and consistent side padding.
- Scrollbar thumb styling is softer and more modern.

## Implementation notes

The polish stays WPF-native and uses reusable styles/control templates in `MainWindow.xaml`:

- `RibbonButton` and `DangerButton` for compact ribbon actions.
- `PanelShell` for elevated workspace cards.
- `PanelHeader` for gradient card headers.
- `ModernGridCellStyle` for readable selected cells.
- `ModernScrollThumb` for modern scroll thumb visuals.

No injection engine behavior was changed in this polish pass.
