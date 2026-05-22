using System;
using System.IO;
using System.Text;
using IL2CPP;
using UnityEngine;

namespace YgoMasterClient
{
    // Dev-only debug helper: writes dumps/logs to a _tmp folder for inspection while
    // building the Roguelike UI (GameObject hierarchies, state, etc.). Not for production.
    static class RoguelikeDebug
    {
        public static string Dir = @"D:\www\ygomaster-fork\YgoMaster\_tmp";

        public static void Write(string name, string content)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Path.Combine(Dir, name), content);
                Console.WriteLine("[RoguelikeDebug] wrote _tmp/" + name);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[RoguelikeDebug] write EX: " + ex);
            }
        }

        // Dump a GameObject's hierarchy + components to _tmp/<name>.
        public static void DumpGO(string name, IntPtr go)
        {
            if (go == IntPtr.Zero)
            {
                Console.WriteLine("[RoguelikeDebug] " + name + ": null GameObject");
                return;
            }
            Write(name, GameObject.Dump(go));
        }

        // Compact skeleton: indented GameObject names + their component type names, no
        // member values, capped at maxDepth. Small + readable (GameObject.Dump bloats to
        // MBs on big screens). Used to scout reusable UI prefabs.
        public static string DumpTree(IntPtr go, int maxDepth)
        {
            StringBuilder sb = new StringBuilder();
            if (go != IntPtr.Zero) DumpTreeInto(sb, GameObject.GetTransform(go), 0, maxDepth);
            return sb.ToString();
        }

        static void DumpTreeInto(StringBuilder sb, IntPtr transform, int depth, int maxDepth)
        {
            IntPtr obj = Component.GetGameObject(transform);
            sb.Append(' ', depth * 2).Append(UnityObject.GetName(obj));
            IntPtr[] comps = GameObject.GetComponents(obj);
            if (comps != null && comps.Length > 0)
            {
                sb.Append("  [");
                for (int i = 0; i < comps.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    IntPtr cls = IL2CPP.Import.Object.il2cpp_object_get_class(comps[i]);
                    sb.Append(System.Runtime.InteropServices.Marshal.PtrToStringAnsi(
                        IL2CPP.Import.Class.il2cpp_class_get_name(cls)));
                }
                sb.Append(']');
            }
            sb.Append('\n');
            if (depth >= maxDepth) return;
            int n = Transform.GetChildCount(transform);
            for (int i = 0; i < n; i++)
                DumpTreeInto(sb, Transform.GetChild(transform, i), depth + 1, maxDepth);
        }

        struct V2 { public float x, y; public override string ToString() { return "(" + x + ", " + y + ")"; } }
        struct V3 { public float x, y, z; public override string ToString() { return "(" + x + ", " + y + ", " + z + ")"; } }
        struct RectF { public float x, y, w, h; public override string ToString() { return "x=" + x + " y=" + y + " w=" + w + " h=" + h; } }

        static IntPtr _rectType, _scrollRectType, _canvasGroupType;
        static IL2Property _anchorMin, _anchorMax, _pivot, _anchoredPos, _sizeDelta, _rect, _localScale;
        static IL2Property _srContent, _srViewport, _srHorizontal, _srVertical;
        static IL2Property _srMovementType, _srInertia, _srDecel, _srElasticity, _srSensitivity, _srHScrollbar, _srVScrollbar, _srEnabled;
        static IL2Property _cgAlpha;
        static bool _dumpInit;

        static void InitDump()
        {
            if (_dumpInit) return;
            _dumpInit = true;
            IL2Class rect = Assembler.GetAssembly("UnityEngine.CoreModule").GetClass("RectTransform", "UnityEngine");
            _rectType = rect.IL2Typeof();
            _anchorMin = rect.GetProperty("anchorMin");
            _anchorMax = rect.GetProperty("anchorMax");
            _pivot = rect.GetProperty("pivot");
            _anchoredPos = rect.GetProperty("anchoredPosition");
            _sizeDelta = rect.GetProperty("sizeDelta");
            _rect = rect.GetProperty("rect");
            _localScale = Assembler.GetAssembly("UnityEngine.CoreModule").GetClass("Transform", "UnityEngine").GetProperty("localScale");
            IL2Class sr = Assembler.GetAssembly("UnityEngine.UI").GetClass("ScrollRect", "UnityEngine.UI");
            _scrollRectType = sr.IL2Typeof();
            _srContent = sr.GetProperty("content");
            _srViewport = sr.GetProperty("viewport");
            _srHorizontal = sr.GetProperty("horizontal");
            _srVertical = sr.GetProperty("vertical");
            _srMovementType = sr.GetProperty("movementType");
            _srInertia = sr.GetProperty("inertia");
            _srDecel = sr.GetProperty("decelerationRate");
            _srElasticity = sr.GetProperty("elasticity");
            _srSensitivity = sr.GetProperty("scrollSensitivity");
            _srHScrollbar = sr.GetProperty("horizontalScrollbar");
            _srVScrollbar = sr.GetProperty("verticalScrollbar");
            _srEnabled = Assembler.GetAssembly("UnityEngine.CoreModule").GetClass("Behaviour", "UnityEngine").GetProperty("enabled");
            IL2Class cg = Assembler.GetAssembly("UnityEngine.UIModule").GetClass("CanvasGroup", "UnityEngine");
            _canvasGroupType = cg.IL2Typeof();
            _cgAlpha = cg.GetProperty("alpha");
        }

        // Dump key component state (RectTransform / ScrollRect / CanvasGroup + component list) of
        // a GameObject — for diagnosing layout/scroll issues.
        public static string DumpComponentsState(IntPtr go)
        {
            if (go == IntPtr.Zero) return "null GameObject";
            InitDump();
            StringBuilder sb = new StringBuilder();
            sb.Append(UnityObject.GetName(go)).Append('\n');

            IntPtr rt = GameObject.GetComponent(go, _rectType);
            if (rt != IntPtr.Zero)
            {
                sb.Append("  RectTransform:\n");
                sb.Append("    anchorMin=").Append(GetV2(_anchorMin, rt)).Append('\n');
                sb.Append("    anchorMax=").Append(GetV2(_anchorMax, rt)).Append('\n');
                sb.Append("    pivot=").Append(GetV2(_pivot, rt)).Append('\n');
                sb.Append("    anchoredPosition=").Append(GetV2(_anchoredPos, rt)).Append('\n');
                sb.Append("    sizeDelta=").Append(GetV2(_sizeDelta, rt)).Append('\n');
                sb.Append("    rect=").Append(GetRect(_rect, rt)).Append('\n');
                sb.Append("    localScale=").Append(GetV3(_localScale, rt)).Append('\n');
            }

            IntPtr sr = GameObject.GetComponent(go, _scrollRectType);
            if (sr != IntPtr.Zero)
            {
                sb.Append("  ScrollRect:\n");
                sb.Append("    enabled=").Append(GetBool(_srEnabled, sr)).Append('\n');
                sb.Append("    content=").Append(RefName(_srContent, sr)).Append('\n');
                sb.Append("    viewport=").Append(RefName(_srViewport, sr)).Append('\n');
                sb.Append("    horizontal=").Append(GetBool(_srHorizontal, sr)).Append('\n');
                sb.Append("    vertical=").Append(GetBool(_srVertical, sr)).Append('\n');
                sb.Append("    movementType=").Append(GetInt(_srMovementType, sr)).Append('\n');
                sb.Append("    inertia=").Append(GetBool(_srInertia, sr)).Append('\n');
                sb.Append("    decelerationRate=").Append(GetFloat(_srDecel, sr)).Append('\n');
                sb.Append("    elasticity=").Append(GetFloat(_srElasticity, sr)).Append('\n');
                sb.Append("    scrollSensitivity=").Append(GetFloat(_srSensitivity, sr)).Append('\n');
                sb.Append("    horizontalScrollbar=").Append(RefName(_srHScrollbar, sr)).Append('\n');
                sb.Append("    verticalScrollbar=").Append(RefName(_srVScrollbar, sr)).Append('\n');
            }

            IntPtr cg2 = GameObject.GetComponent(go, _canvasGroupType);
            if (cg2 != IntPtr.Zero) sb.Append("  CanvasGroup.alpha=").Append(GetFloat(_cgAlpha, cg2)).Append('\n');

            IntPtr[] comps = GameObject.GetComponents(go);
            if (comps != null)
            {
                sb.Append("  components: ");
                for (int i = 0; i < comps.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    IntPtr cls = Import.Object.il2cpp_object_get_class(comps[i]);
                    sb.Append(System.Runtime.InteropServices.Marshal.PtrToStringAnsi(Import.Class.il2cpp_class_get_name(cls)));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        static string GetV2(IL2Property p, IntPtr comp) { try { return p.GetGetMethod().Invoke(comp).GetValueRef<V2>().ToString(); } catch { return "?"; } }
        static string GetV3(IL2Property p, IntPtr comp) { try { return p.GetGetMethod().Invoke(comp).GetValueRef<V3>().ToString(); } catch { return "?"; } }
        static string GetRect(IL2Property p, IntPtr comp) { try { return p.GetGetMethod().Invoke(comp).GetValueRef<RectF>().ToString(); } catch { return "?"; } }
        static string GetBool(IL2Property p, IntPtr comp) { try { return p.GetGetMethod().Invoke(comp).GetValueRef<bool>().ToString(); } catch { return "?"; } }
        static string GetFloat(IL2Property p, IntPtr comp) { try { return p.GetGetMethod().Invoke(comp).GetValueRef<float>().ToString(); } catch { return "?"; } }
        static string GetInt(IL2Property p, IntPtr comp) { try { return p.GetGetMethod().Invoke(comp).GetValueRef<int>().ToString(); } catch { return "?"; } }
        static string RefName(IL2Property p, IntPtr comp)
        {
            try
            {
                IL2Object v = p.GetGetMethod().Invoke(comp);
                if (v == null || v.ptr == IntPtr.Zero) return "null";
                return UnityObject.GetName(Component.GetGameObject(v.ptr));
            }
            catch { return "?"; }
        }

        // Append a timestamped line to _tmp/roguelike.log.
        public static void Log(string line)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, "roguelike.log"),
                    "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line + "\n");
            }
            catch { }
        }
    }
}
