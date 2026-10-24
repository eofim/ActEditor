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
