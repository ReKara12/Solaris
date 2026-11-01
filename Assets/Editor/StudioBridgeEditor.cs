#if UNITY_EDITOR
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace AutonomousStudio.EditorBridge
{
    [InitializeOnLoad]
    public static class StudioBridgeEditor
    {
        private static HttpListener _listener;
        private static Thread _serverThread;
        private static readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private static readonly List<string> _compilerErrors = new List<string>();
        private static readonly List<string> _runtimeErrors = new List<string>();
        private static readonly object _logLock = new object();
        private const int PORT = 8765;

        static StudioBridgeEditor()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;

            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompiled;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;

            Application.logMessageReceivedThreaded -= OnLogMessage;
            Application.logMessageReceivedThreaded += OnLogMessage;

            StartServer();
            AssemblyReloadEvents.beforeAssemblyReload += StopServer;
            EditorApplication.quitting += StopServer;
        }

        private static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            lock (_logLock)
            {
                foreach (var m in messages)
                {
                    if (m.type == CompilerMessageType.Error)
                    {
                        string entry = $"{m.file}({m.line},{m.column}): {m.message}";
                        if (!_compilerErrors.Contains(entry))
                            _compilerErrors.Add(entry);
                    }
                }
            }
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
            {
                lock (_logLock)
                {
                    if (condition.Contains("CS") && condition.Contains("error "))
                    {
                        if (!_compilerErrors.Contains(condition))
                            _compilerErrors.Add(condition);
                    }
                    else
                    {
                        string entry = $"{condition} | {stackTrace.Split('\n').FirstOrDefault()}";
                        if (_runtimeErrors.Count < 30 && !_runtimeErrors.Contains(entry))
                            _runtimeErrors.Add(entry);
                    }
                }
            }
        }

        private static void StartServer()
        {
            if (_listener != null && _listener.IsListening) return;
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{PORT}/");
                _listener.Start();
                _serverThread = new Thread(ListenLoop) { IsBackground = true };
                _serverThread.Start();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StudioBridge] Port {PORT} dinlenemedi: {e.Message}");
            }
        }

        private static void StopServer()
        {
            try
            {
                _listener?.Stop();
                _listener?.Close();
                _listener = null;
            }
            catch { }
        }

        private static void OnEditorUpdate()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try { action.Invoke(); }
                catch (Exception ex) { Debug.LogError($"[StudioBridge MainThread Error] {ex}"); }
            }
        }

        private static void ListenLoop()
        {
            while (_listener != null && _listener.IsListening)
            {
                try
                {
                    var ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
                }
                catch { break; }
            }
        }

        private static string RunOnMainThread(Func<string> func, int timeoutMs = 15000)
        {
            string result = "{\"ok\":false,\"error\":\"Timeout\"}";
            using (var evt = new ManualResetEventSlim(false))
            {
                _mainThreadQueue.Enqueue(() =>
                {
                    try { result = func(); }
                    catch (Exception e) { result = "{\"ok\":false,\"error\":\"" + EscapeJson(e.Message) + "\"}"; }
                    finally { evt.Set(); }
                });
                evt.Wait(timeoutMs);
            }
            return result;
        }

        private static void HandleRequest(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();
            string body = "";
            if (ctx.Request.HasEntityBody)
            {
                using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    body = reader.ReadToEnd();
            }

            string responseJson = "{\"ok\":true}";

            if (path == "/status")
            {
                responseJson = RunOnMainThread(() =>
                {
                    lock (_logLock)
                    {
                        string errs = string.Join("||", _compilerErrors);
                        string rErrs = string.Join("||", _runtimeErrors);
                        return $"{{\"ok\":true,\"isCompiling\":{(EditorApplication.isCompiling ? "true" : "false")},\"isPlaying\":{(EditorApplication.isPlaying ? "true" : "false")},\"compilerErrors\":\"{EscapeJson(errs)}\",\"runtimeErrors\":\"{EscapeJson(rErrs)}\",\"scene\":\"{EscapeJson(SceneManager.GetActiveScene().name)}\"}}";
                    }
                });
            }
            else if (path == "/refresh")
            {
                responseJson = RunOnMainThread(() =>
                {
                    lock (_logLock) { _compilerErrors.Clear(); _runtimeErrors.Clear(); }
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    return $"{{\"ok\":true,\"isCompiling\":{(EditorApplication.isCompiling ? "true" : "false")}}}";
                });
            }
            else if (path == "/hierarchy")
            {
                responseJson = RunOnMainThread(() =>
                {
                    var roots = SceneManager.GetActiveScene().GetRootGameObjects();
                    var list = new List<string>();
                    foreach (var go in roots)
                    {
                        var comps = go.GetComponents<Component>()
                            .Where(c => c != null)
                            .Select(c => c.GetType().Name);
                        list.Add($"{go.name} [{string.Join(", ", comps)}]");
                    }
                    return $"{{\"ok\":true,\"objects\":\"{EscapeJson(string.Join("; ", list))}\"}}";
                });
            }
            else if (path == "/bootstrap_scene")
            {
                responseJson = RunOnMainThread(() => BootstrapAndCapture(), 20000);
            }
            else if (path == "/play")
            {
                responseJson = RunOnMainThread(() =>
                {
                    EditorApplication.isPlaying = true;
                    return $"{{\"ok\":true,\"isPlaying\":true}}";
                });
            }
            else if (path == "/stop")
            {
                responseJson = RunOnMainThread(() =>
                {
                    EditorApplication.isPlaying = false;
                    return $"{{\"ok\":true,\"isPlaying\":false}}";
                });
            }
            else if (path == "/start_game")
            {
                responseJson = RunOnMainThread(() =>
                {
                    var gm = UnityEngine.Object.FindFirstObjectByType<SolarisGameManager>();
                    if (gm != null)
                    {
                        gm.StartGame();
                        return "{\"ok\":true,\"started\":true}";
                    }
                    return "{\"ok\":false,\"error\":\"No GameManager\"}";
                });
            }
            else if (path == "/ui_debug")
            {
                responseJson = RunOnMainThread(() =>
                {
                    var ui = UnityEngine.Object.FindFirstObjectByType<SolarisUIManager>();
                    if (ui == null) return "{\"ok\":false,\"error\":\"No UIManager\"}";
                    var texts = ui.GetComponentsInChildren<UnityEngine.UI.Text>(true);
                    var list = new List<string>();
                    foreach (var t in texts)
                    {
                        string fontName = t.font != null ? t.font.name : "NULL_FONT";
                        list.Add($"{t.gameObject.name}: '{t.text}' ({fontName}, active={t.gameObject.activeInHierarchy})");
                    }
                    return $"{{\"ok\":true,\"count\":{texts.Length},\"texts\":[\"{string.Join("\",\"", list.Select(EscapeJson))}\"]}}";
                });
            }
            else if (path == "/terrain_debug")
            {
                responseJson = RunOnMainThread(() =>
                {
                    var world = UnityEngine.Object.FindFirstObjectByType<SandGridWorld>();
                    if (world == null) return "{\"ok\":false,\"error\":\"No SandGridWorld\"}";
                    var mf = world.GetComponent<MeshFilter>();
                    var mesh = mf != null ? mf.sharedMesh : null;
                    string colInfo = "NO_MESH";
                    if (mesh != null)
                    {
                        var cols = mesh.colors;
                        if (cols != null && cols.Length > 0)
                        {
                            colInfo = $"Count={cols.Length}, C0={cols[0]}, Center={cols[cols.Length / 2]}";
                        }
                        else colInfo = "COLORS_NULL_OR_EMPTY";
                    }
                    var uvs = mesh != null ? mesh.uv : null;
                    string uvInfo = uvs != null && uvs.Length > 0 ? $"Count={uvs.Length}, UV0={uvs[0]}, Center={uvs[uvs.Length/2]}" : "NO_UVS";
                    var mr = world.GetComponent<MeshRenderer>();
                    if (mr == null || mr.sharedMaterial == null) return "{\"ok\":false,\"error\":\"No MeshRenderer or Material\"}";
                    var mat = mr.sharedMaterial;
                    string shName = mat.shader != null ? mat.shader.name : "NULL";
                    var mainTex = mat.GetTexture("_MainTex");
                    string mainTexInfo = mainTex != null ? $"{mainTex.name} ({mainTex.width}x{mainTex.height}, wrap={mainTex.wrapMode})" : "NULL";
                    return $"{{\"ok\":true,\"shader\":\"{EscapeJson(shName)}\",\"uvs\":\"{uvInfo}\",\"mainTex\":\"{mainTexInfo}\"}}";
                });
            }
            else if (path == "/raycast_test")
            {
                responseJson = RunOnMainThread(() =>
                {
                    var allMrs = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
                    var list = new List<string>();
                    foreach (var m in allMrs)
                    {
                        if (m.bounds.extents.x > 30f)
                        {
                            string matName = m.sharedMaterial != null ? m.sharedMaterial.name : "NULL";
                            string shName = (m.sharedMaterial != null && m.sharedMaterial.shader != null) ? m.sharedMaterial.shader.name : "NULL";
                            list.Add($"{m.gameObject.name} (pos={m.transform.position}, extents={m.bounds.extents}, mat={matName}, shader={shName})");
                        }
                    }
                    return $"{{\"ok\":true,\"largeRenderers\":[\"{string.Join("\",\"", list.Select(EscapeJson))}\"]}}";
                });
            }
            else if (path == "/capture_screenshot")
            {
                responseJson = RunOnMainThread(() =>
                {
                    Camera cam = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
                    if (cam == null) return "{\"ok\":false,\"error\":\"No camera\"}";
                    string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                    string shotPath = Path.Combine(projectRoot, "screenshot.png");
                    int w = 1280, h = 720;
                    var rt = new RenderTexture(w, h, 24);
                    var prev = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    cam.targetTexture = prev;
                    RenderTexture.active = null;
                    UnityEngine.Object.DestroyImmediate(rt);
                    File.WriteAllBytes(shotPath, tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                    return "{\"ok\":true,\"captured\":true}";
                });
            }

            byte[] buf = Encoding.UTF8.GetBytes(responseJson);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = buf.Length;
            ctx.Response.OutputStream.Write(buf, 0, buf.Length);
            ctx.Response.OutputStream.Close();
        }

        private static string BootstrapAndCapture()
        {
            // 1. Ensure Directional Light & Camera exist
            if (UnityEngine.Object.FindFirstObjectByType<Light>() == null)
            {
                var lightGo = new GameObject("Directional Light");
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Directional;
                l.intensity = 1.2f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            Camera cam = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
                camGo.transform.position = new Vector3(0f, 8f, -14f);
                camGo.transform.LookAt(Vector3.zero);
            }

            // 2. Idempotent Root Setup: Remove old AutonomousStudioRoot so objects never duplicate!
            var oldRoot = GameObject.Find("[AutonomousStudioRoot]");
            if (oldRoot != null)
                UnityEngine.Object.DestroyImmediate(oldRoot);

            var rootGo = new GameObject("[AutonomousStudioRoot]");

            // 3. Find and attach generated Bootstrapper / Manager MonoBehaviours from Assembly-CSharp
            var attachedTypes = new List<string>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.GetName().Name.Contains("Assembly-CSharp")) continue;
                foreach (var t in asm.GetTypes())
                {
                    if (!t.IsSubclassOf(typeof(MonoBehaviour)) || t.IsAbstract) continue;
                    string n = t.Name.ToLowerInvariant();
                    if (n.Contains("bootstrap") || n.Contains("gamemanager") || n.Contains("scenesetup") || n.Contains("worldmanager") || n.Contains("gamecontroller"))
                    {
                        var comp = rootGo.AddComponent(t);
                        attachedTypes.Add(t.Name);
                        // If it has a public BuildScene() or Initialize() method, invoke it for preview
                        var initMethod = t.GetMethod("BuildScene") ?? t.GetMethod("SetupScene") ?? t.GetMethod("Initialize");
                        if (initMethod != null && initMethod.GetParameters().Length == 0)
                        {
                            try { initMethod.Invoke(comp, null); }
                            catch (Exception ex) { Debug.LogWarning($"[StudioBridge Init Invoke] {t.Name}: {ex.Message}"); }
                        }
                    }
                }
            }

            // 4. Save Scene
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scenes"));
            string scenePath = "Assets/Scenes/MainScene.unity";
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), scenePath);

            // 5. Render Camera Screenshot to project root screenshot.png
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string shotPath = Path.Combine(projectRoot, "screenshot.png");
            try
            {
                cam = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
                if (cam != null)
                {
                    int w = 1280, h = 720;
                    var rt = new RenderTexture(w, h, 24);
                    var prev = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    cam.targetTexture = prev;
                    RenderTexture.active = null;
                    UnityEngine.Object.DestroyImmediate(rt);
                    File.WriteAllBytes(shotPath, tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[StudioBridge Screenshot] {e.Message}");
            }

            return $"{{\"ok\":true,\"attached\":\"{EscapeJson(string.Join(", ", attachedTypes))}\",\"screenshot\":\"screenshot.png\"}}";
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
#endif
