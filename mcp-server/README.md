# ActEditor MCP (Windows)
This is an **early MVP** for the fork's in-process named-pipe bridge. The bridge is started from the WPF ActEditor window; the MCP stdio server must run **on the same Windows machine and user account**.

## Setup
1. Build ActEditor in Visual Studio (Windows, .NET Framework 4.8). This environment has not compiled the desktop application.
2. Open ActEditor and load an existing ACT/SPR.
3. Install Node.js 20+ and configure your MCP client:
```json
{"mcpServers":{"acteditor":{"command":"node","args":["C:\\path\\to\\ActEditor\\mcp-server\\src\\index.js"]}}}
```
4. Use `act_ping`, `act_get_state`, `act_export_sprites`, `act_plan_import`, then `act_apply_import` with `confirm: true`.

## Safety and current limitations
- Never expose the named pipe to remote users; same-user local Windows bridge only.
- Export to a **new** directory, then edit existing files. A manifest tracks original image hashes and document revision.
- Import does **not** save automatically. Apply only after reviewing plan.
- Indexed8 images sharing a palette are rejected if replacement bitmap palette differs. Full automatic palette recolor and visual previews are not implemented in this MVP.
- The bridge is single-instance; do not run two ActEditor windows concurrently.
- Saving supports existing loose ACT/SPR paths, not new unsaved documents or archive entries.
- Not yet Windows build-tested. Verify project dependencies, functionality and transaction rollback on representative ACT/SPR files before using production assets.

## Recolor via AI
1. Select BGRA32 or Indexed8 sprite images (Ctrl/Shift on the sprite strip) and call `act_get_state`.
2. Call `act_plan_tone` with `document_id`, `expected_revision`, `mode` and tone adjustments. Values: `hue` -180..180 degrees; `saturation`, `lightness`, `brightness`, `contrast` -1..1. Optional `use_target_color`, `target_color: "#RRGGBB"`, `hue_tolerance` 0..0.5, `minimum_saturation` 0..1.
3. Inspect the returned changed counts and `other_sprites_affected` list.
4. Apply with `act_apply_tone` using `plan_id`, matching revision and `confirm: true`. Indexed8 recolor touching other sprites additionally requires `allow_shared_palette: true`.
5. Review and save explicitly. In-memory Undo is available.

This tone effect is adapted from [zakeph/ActEditor, branch mio](https://github.com/zakeph/ActEditor/tree/mio). The rest of that fork's binary resources, personal scripts and bulk generated images are intentionally excluded.
