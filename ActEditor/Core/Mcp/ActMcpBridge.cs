using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using ActEditor.Core.WPF.Dialogs;
using GRF.FileFormats.SprFormat;
using GRF.Image;

namespace ActEditor.Core.Mcp {
    // Local, single-instance bridge. All document access runs on the WPF dispatcher.
    public sealed class ActMcpBridge : IDisposable {
        private const string PipeName = "ActEditor.MCP";
        private readonly ActEditorWindow _window;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        private volatile bool _running;
        private NamedPipeServerStream _listener;
        private ImportPlan _plan;

        private sealed class ImportItem {
            public int Index;
            public string Path;
            public string FileHash;
            public string SpriteHash;
        }
        private sealed class ImportPlan {
            public string Id;
            public string Document;
            public int Revision;
            public List<ImportItem> Items;
        }

        public ActMcpBridge(ActEditorWindow window) { _window = window; }

        public void Start() {
            if (_running) return;
            _running = true;
            Task.Run((Action)Listen);
        }

        private void Listen() {
            while (_running) {
                try {
                    var access = new PipeSecurity();
                    access.SetAccessRuleProtection(true, false);
                    access.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,
                        PipeAccessRights.ReadWrite, AccessControlType.Allow));
                    using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                               PipeTransmissionMode.Byte, PipeOptions.None, 4096, 4096, access)) {
                        _listener = pipe;
                        pipe.WaitForConnection();
                        if (!_running) break;
                        using (var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, true))
                        using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true }) {
                            string line = reader.ReadLine();
                            if (line == null) continue;
                            object response;
                            try {
                                if (line.Length > 131072) throw new InvalidOperationException("Request too large.");
                                var request = _json.Deserialize<Dictionary<string, object>>(line);
                                response = _window.Dispatcher.Invoke(new Func<object>(() => {
                                    try { return new { ok = true, result = Dispatch(request) }; }
                                    catch (Exception ex) { return new { ok = false, error = ex.Message }; }
                                }));
                            }
                            catch (Exception ex) { response = new { ok = false, error = ex.Message }; }
                            writer.WriteLine(_json.Serialize(response));
                        }
                    }
                }
                catch (IOException) { /* Client closed or pipe was disposed. */ }
                catch (ObjectDisposedException) { }
                catch (Exception) { /* Do not crash the editor if IPC cannot start. */ }
                finally { _listener = null; }
            }
        }

        private static object Arg(IDictionary<string, object> args, string key) {
            object value;
            return args != null && args.TryGetValue(key, out value) ? value : null;
        }
        private static string Str(IDictionary<string, object> args, string key, string fallback = null) {
            var value = Arg(args, key);
            return value == null ? fallback : Convert.ToString(value);
        }
        private static int Int(IDictionary<string, object> args, string key, int fallback = 0) {
            var value = Arg(args, key);
            return value == null ? fallback : Convert.ToInt32(value);
        }
        private static bool Bool(IDictionary<string, object> args, string key) {
            var value = Arg(args, key);
            return value != null && Convert.ToBoolean(value);
        }
        private static IDictionary<string, object> Dict(object value) {
            var map = value as IDictionary<string, object>;
            if (map == null) throw new InvalidOperationException("Invalid manifest entry.");
            return map;
        }
        private static int[] IntArray(object value) {
            var values = value as object[];
            if (values == null) throw new InvalidOperationException("ids must be an array.");
            return values.Select(Convert.ToInt32).ToArray();
        }
        private static string DocumentId(TabAct tab) {
            return tab.Act.LoadedPath ?? "";
        }
        private TabAct Current(IDictionary<string, object> args) {
            var tab = _window.TabEngine.GetCurrentTab();
            if (tab == null || tab.Act == null) throw new InvalidOperationException("No ACT document is open.");
            var id = Str(args, "document_id");
            if (id != null && id != DocumentId(tab))
                throw new InvalidOperationException("Active document changed; re-read state.");
            return tab;
        }
        private static int Revision(TabAct tab) { return tab.Act.Commands.CommandIndex; }
        private static void CheckRevision(TabAct tab, IDictionary<string, object> args) {
            if (Arg(args, "expected_revision") == null || Int(args, "expected_revision") != Revision(tab))
                throw new InvalidOperationException("Document revision differs; refresh state before editing.");
        }
        private static string Sha(byte[] bytes) {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        private static string FileSha(string path) {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static string SpriteHash(GrfImage image) {
            byte[] pixels = image.Pixels;
            byte[] palette = image.GrfImageType == GrfImageType.Indexed8 ? image.Palette : new byte[0];
            var combined = new byte[pixels.Length + palette.Length + 8];
            Buffer.BlockCopy(BitConverter.GetBytes(image.Width), 0, combined, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(image.Height), 0, combined, 4, 4);
            Buffer.BlockCopy(pixels, 0, combined, 8, pixels.Length);
            Buffer.BlockCopy(palette, 0, combined, 8 + pixels.Length, palette.Length);
            return Sha(combined);
        }
        private static object Describe(TabAct tab) {
            var spr = tab.Act.Sprite;
            return new {
                document_id = DocumentId(tab),
                path = tab.Act.LoadedPath,
                revision = Revision(tab),
                modified = tab.Act.Commands.IsModified,
                action = tab.SelectedAction,
                frame = tab.SelectedFrame,
                sprite_count = spr.NumberOfImagesLoaded,
                indexed_count = spr.NumberOfIndexed8Images,
                bgra_count = spr.NumberOfBgra32Images,
                selected_sprite_ids = tab.SpriteSelector.GetSelectedSpriteIds(),
                selected_layer_ids = tab.SelectionEngine.CurrentlySelected.OrderBy(x => x).ToArray()
            };
        }
        private object Dispatch(Dictionary<string, object> request) {
            string method = Str(request, "method");
            var args = Arg(request, "params") as IDictionary<string, object>
                       ?? new Dictionary<string, object>();
            if (method == "ping") return new { connected = true, app = "ActEditor", protocol = 1 };
            if (method == "list_documents") return _window.TabEngine.GetTabs().Where(t => t.Act != null)
                .Select(t => new { document_id = DocumentId(t), path = t.Act.LoadedPath, modified = t.Act.Commands.IsModified }).ToArray();
            if (method == "get_state" || method == "get_selection") return Describe(Current(args));
            if (method == "list_sprites") {
                var tab = Current(args);
                int start = Int(args, "offset"), limit = Int(args, "limit", 100);
                if (start < 0 || limit < 1 || limit > 500) throw new InvalidOperationException("Invalid pagination.");
                return tab.Act.Sprite.Images.Skip(start).Take(limit).Select((im, i) => new {
                    index = start + i, type = im.GrfImageType.ToString(),
                    width = im.Width, height = im.Height, sha256 = SpriteHash(im)
                }).ToArray();
            }
            if (method == "set_selection") {
                var tab = Current(args);
                var ids = IntArray(Arg(args, "ids"));
                if (ids.Any(i => i < 0 || i >= tab.Act.Sprite.NumberOfImagesLoaded))
                    throw new InvalidOperationException("Sprite index outside SPR.");
                tab.SpriteSelector.SetSelectedSpriteIds(ids, Str(args, "mode", "replace"));
                return Describe(tab);
            }
            if (method == "export_sprites") return Export(Current(args), args);
            if (method == "plan_import") return PlanImport(Current(args), args);
            if (method == "apply_import") return ApplyImport(Current(args), args);
            if (method == "undo" || method == "redo") {
                var tab = Current(args);
                CheckRevision(tab, args);
                if (method == "undo") tab.Undo(); else tab.Redo();
                _plan = null;
                return Describe(tab);
            }
            if (method == "save_document") {
                var tab = Current(args);
                CheckRevision(tab, args);
                if (!Bool(args, "confirm")) throw new InvalidOperationException("Saving requires confirm=true.");
                if (tab.IsNew || !File.Exists(tab.Act.LoadedPath))
                    throw new InvalidOperationException("Save is only supported for an existing ACT on disk (not GRF).");
                if (!_window.TabEngine.Save(tab)) throw new IOException("ActEditor could not save document.");
                return Describe(tab);
            }
            throw new InvalidOperationException("Unknown MCP method: " + method);
        }
        private object Export(TabAct tab, IDictionary<string, object> args) {
            string folder = Str(args, "output_dir");
            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathRooted(folder))
                throw new InvalidOperationException("An absolute output_dir is required.");
            string scope = Str(args, "scope", "selected");
            int count = tab.Act.Sprite.NumberOfImagesLoaded;
            int[] ids;
            if (scope == "all") ids = Enumerable.Range(0, count).ToArray();
            else if (scope == "selected") ids = tab.SpriteSelector.GetSelectedSpriteIds();
            else if (scope == "ids") ids = IntArray(Arg(args, "ids"));
            else throw new InvalidOperationException("scope must be all, selected or ids.");
            ids = ids.Distinct().OrderBy(i => i).ToArray();
            if (ids.Length == 0 || ids.Any(i => i < 0 || i >= count))
                throw new InvalidOperationException("No valid sprites selected.");
            Directory.CreateDirectory(folder);
            string manifestFile = Path.Combine(folder, "manifest.json");
            if (File.Exists(manifestFile)) throw new IOException("Manifest exists. Export to a fresh folder.");
            var files = new List<object>();
            foreach (var id in ids) {
                var image = tab.Act.Sprite.Images[id].Copy();
                string extension = image.GrfImageType == GrfImageType.Indexed8 ? ".bmp" : ".png";
                string filename = "sprite_" + id.ToString("D4") + extension;
                string destination = Path.Combine(folder, filename);
                if (File.Exists(destination)) throw new IOException("File exists: " + destination);
                image.Save(destination);
                files.Add(new {
                    index = id, file = filename, type = image.GrfImageType.ToString(),
                    width = image.Width, height = image.Height,
                    sprite_sha256 = SpriteHash(tab.Act.Sprite.Images[id]),
                    file_sha256 = FileSha(destination)
                });
            }
            File.WriteAllText(manifestFile, _json.Serialize(new {
                manifest_version = 1, document_id = DocumentId(tab), revision = Revision(tab), images = files
            }), new UTF8Encoding(false));
            return new { manifest_path = manifestFile, exported = ids.Length, ids };
        }
        private object PlanImport(TabAct tab, IDictionary<string, object> args) {
            string manifestFile = Str(args, "manifest_path");
            if (string.IsNullOrEmpty(manifestFile) || !File.Exists(manifestFile))
                throw new FileNotFoundException("Manifest not found.");
            var manifest = Dict(_json.DeserializeObject(File.ReadAllText(manifestFile)));
            if (Int(manifest, "manifest_version") != 1 || Str(manifest, "document_id") != DocumentId(tab))
                throw new InvalidOperationException("Manifest belongs to another document.");
            if (Int(manifest, "revision") != Revision(tab))
                throw new InvalidOperationException("Document was edited since export. Export again.");
            var entries = Arg(manifest, "images") as object[];
            if (entries == null) throw new InvalidOperationException("Missing image entries.");
            var pending = new List<ImportItem>();
            var unchanged = 0;
            string root = Path.GetDirectoryName(Path.GetFullPath(manifestFile));
            foreach (var item in entries) {
                var row = Dict(item);
                int index = Int(row, "index");
                string name = Str(row, "file");
                if (index < 0 || index >= tab.Act.Sprite.NumberOfImagesLoaded || string.IsNullOrEmpty(name) ||
                    name != Path.GetFileName(name)) throw new InvalidOperationException("Invalid manifest index or filename.");
                var current = tab.Act.Sprite.Images[index];
                string spriteHash = SpriteHash(current);
                if (spriteHash != Str(row, "sprite_sha256"))
                    throw new InvalidOperationException("Sprite " + index + " changed since export.");
                string source = Path.Combine(root, name);
                if (!File.Exists(source)) throw new FileNotFoundException("Edited image not found: " + source);
                string hash = FileSha(source);
                if (hash == Str(row, "file_sha256")) { unchanged++; continue; }
                var replacement = new GrfImage(source);
                if (current.GrfImageType != replacement.GrfImageType)
                    throw new InvalidOperationException("Sprite " + index + ": type changed. Indexed8 must remain an indexed BMP.");
                if (current.GrfImageType == GrfImageType.Indexed8 &&
                    !current.Palette.SequenceEqual(replacement.Palette))
                    throw new InvalidOperationException("Sprite " + index + ": indexed palette differs. Shared palette conversion is unsafe.");
                pending.Add(new ImportItem { Index = index, Path = source, FileHash = hash, SpriteHash = spriteHash });
            }
            _plan = new ImportPlan { Id = Guid.NewGuid().ToString("N"), Document = DocumentId(tab), Revision = Revision(tab), Items = pending };
            return new {
                plan_id = _plan.Id, document_id = _plan.Document, revision = _plan.Revision,
                changed = pending.Select(p => p.Index).ToArray(), unchanged,
                warning = "Indexed8 recolors must retain the shared palette. Apply changes in memory; save separately."
            };
        }
        private object ApplyImport(TabAct tab, IDictionary<string, object> args) {
            if (!Bool(args, "confirm")) throw new InvalidOperationException("Apply requires confirm=true.");
            CheckRevision(tab, args);
            var plan = _plan;
            if (plan == null || plan.Id != Str(args, "plan_id") || plan.Document != DocumentId(tab) ||
                plan.Revision != Revision(tab)) throw new InvalidOperationException("Import plan expired. Plan again.");
            foreach (var item in plan.Items) {
                if (FileSha(item.Path) != item.FileHash ||
                    SpriteHash(tab.Act.Sprite.Images[item.Index]) != item.SpriteHash)
                    throw new InvalidOperationException("Files or sprites changed after preview. Plan again.");
            }
            _plan = null;
            if (plan.Items.Count == 0) return new { applied = 0, state = Describe(tab) };
            bool started = false;
            try {
                tab.Act.Commands.ActEditBegin("MCP: replace sprites");
                started = true;
                foreach (var item in plan.Items) {
                    tab.SpriteManager.ReplaceImage(SpriteIndex.FromAbsoluteIndex(item.Index, tab.Act.Sprite),
                        new GrfImage(item.Path));
                }
                tab.Act.Commands.ActEditEnd();
                started = false;
                tab.SpriteSelector.Update();
                tab.UpdatePrimary();
                return new { applied = plan.Items.Count, indices = plan.Items.Select(i => i.Index).ToArray(), state = Describe(tab) };
            }
            catch {
                if (started) tab.Act.Commands.ActCancelEdit();
                throw;
            }
        }
        public void Dispose() {
            _running = false;
            try { _listener?.Dispose(); } catch (IOException) { } catch (ObjectDisposedException) { }
        }
    }
}
