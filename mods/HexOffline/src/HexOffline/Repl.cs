using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Game.Client;
using Game.Shared;
using Reckoning.Game;
using UnityEngine;

namespace HexOffline
{
    /// <summary>
    /// Mini-debugger generique par reflexion : lecture/ecriture de n'importe quel
    /// champ/propriete (public ou prive), a n'importe quelle profondeur, + appel de methodes.
    /// Accessible via le canal de commandes (HexOffline.cmd).
    /// </summary>
    public static class Repl
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static Dictionary<string, object> BuildRefs(StandaloneGameClient gc)
        {
            Dictionary<string, object> refs = new Dictionary<string, object>();
            if (ReferenceEquals(gc, null)) return refs;
            refs["gc"] = gc;
            refs["profile"] = gc.PlayerProfile;
            refs["player"] = gc.Player;
            refs["session"] = gc.Session;
            refs["stateManager"] = gc.StateManager;
            try { refs["main"] = Singleton<Main>.Instance; } catch { }
            try { refs["templates"] = Singleton<TemplateManager>.Instance; } catch { }
            try { refs["ui"] = UIBattle.Instance; } catch { }
            try { refs["gameconfig"] = typeof(GameConfig); } catch { }
            try
            {
                FieldInfo f = typeof(StandaloneGameClient).GetField("m_SessionServer", BindingFlags.Instance | BindingFlags.NonPublic);
                if (!ReferenceEquals(f, null)) refs["server"] = f.GetValue(gc);
                FieldInfo g = typeof(StandaloneGameClient).GetField("m_LocalPlayerMap", BindingFlags.Instance | BindingFlags.NonPublic);
                if (!ReferenceEquals(g, null)) refs["localMap"] = g.GetValue(gc);
            }
            catch { }
            return refs;
        }

        public static string Execute(StandaloneGameClient gc, string[] parts, Dictionary<string, object> refs)
        {
            string verb = parts[0].ToLower();
            switch (verb)
            {
                case "refs": return ListRefs(refs);
                case "types": return Types(parts.Length > 1 ? parts[1] : "");
                case "statics": return Statics(parts.Length > 1 ? parts[1] : "");
                case "get": return Get(parts, refs);
                case "set": return Set(parts, refs);
                case "getref": return GetRef(parts, refs);
                case "call": return Call(parts, refs, staticOnly: true);
                case "callref": return Call(parts, refs, staticOnly: false);
                default: return "commande inconnue. refs|types|statics|get|set|getref|call|callref";
            }
        }

        private static string ListRefs(Dictionary<string, object> refs)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, object> kv in refs)
                sb.Append(kv.Key).Append(" => ").Append(Summarize(kv.Value)).Append('\n');
            return sb.ToString();
        }

        private static string Types(string filter)
        {
            StringBuilder sb = new StringBuilder();
            int n = 0;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string an = asm.GetName().Name;
                if (an != "Assembly-CSharp" && an != "Assembly-CSharp-firstpass") continue;
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (Type t in types)
                {
                    if (string.IsNullOrEmpty(filter) || t.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        sb.Append(t.FullName).Append('\n');
                        if (++n >= 200) { sb.Append("...(tronque)"); return sb.ToString(); }
                    }
                }
            }
            return sb.ToString();
        }

        private static string Statics(string typeName)
        {
            Type t = FindType(typeName);
            if (ReferenceEquals(t, null)) return "type introuvable";
            StringBuilder sb = new StringBuilder();
            foreach (FieldInfo fi in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                object v = null;
                try { v = fi.GetValue(null); } catch { }
                sb.Append("field ").Append(fi.FieldType.Name).Append(' ').Append(fi.Name).Append(" = ").Append(Summarize(v)).Append('\n');
            }
            foreach (PropertyInfo pi in t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                object v = null;
                try { if (pi.CanRead && pi.GetIndexParameters().Length == 0) v = pi.GetValue(null, null); } catch { }
                sb.Append("prop  ").Append(pi.PropertyType.Name).Append(' ').Append(pi.Name).Append(" = ").Append(Summarize(v)).Append('\n');
            }
            return sb.ToString();
        }

        private static string Get(string[] parts, Dictionary<string, object> refs)
        {
            // get <ref> <path>   (regarde d'abord dans les refs, sinon type statique)
            if (parts.Length < 3) return "usage: get <ref|Type> <path>";
            object cur;
            if (refs.TryGetValue(parts[1], out cur))
                return parts[1] + "." + parts[2] + " = " + Summarize(Walk(cur, parts[2]));
            Type t = FindType(parts[1]);
            if (!ReferenceEquals(t, null))
                return parts[1] + "." + parts[2] + " = " + Summarize(WalkStatic(t, parts[2]));
            return "ref/type introuvable: " + parts[1];
        }

        private static string GetRef(string[] parts, Dictionary<string, object> refs)
        {
            return Get(parts, refs);
        }

        private static string Set(string[] parts, Dictionary<string, object> refs)
        {
            if (parts.Length < 4) return "usage: set <ref|Type> <path> <value>";
            string value = string.Join(" ", parts, 3, parts.Length - 3);
            object cur;
            string path = parts[2];
            object target;
            Type t;
            if (refs.TryGetValue(parts[1], out cur)) { target = WalkParent(cur, path); t = ReferenceEquals(target, null) ? null : target.GetType(); }
            else { t = FindType(parts[1]); target = null; }
            if (ReferenceEquals(t, null) && ReferenceEquals(target, null)) return "ref/type introuvable";
            string member = LastMember(path);
            object converted = ConvertArg(value, FindMemberType(target, t, member));
            bool ok = SetMember(target, t, member, converted);
            return ok ? "set ok" : "set echec";
        }

        private static string Call(string[] parts, Dictionary<string, object> refs, bool staticOnly)
        {
            if (parts.Length < 3) return "usage: call <Type> <method> [args] ; callref <ref> <method> [args]";
            object target = null;
            Type t;
            if (staticOnly)
            {
                t = FindType(parts[1]);
                if (ReferenceEquals(t, null)) return "type introuvable";
                if (parts.Length < 4) { } // pas d'arg
            }
            else
            {
                if (!refs.TryGetValue(parts[1], out target)) return "ref introuvable";
                t = target.GetType();
            }
            string method = parts[2];
            string[] args = parts.Length > 3 ? parts[3].Split(',') : new string[0];
            foreach (MethodInfo m in t.GetMethods(All))
            {
                if (m.Name != method) continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length != args.Length) continue;
                object[] vals = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++) vals[i] = ConvertArg(args[i].Trim(), ps[i].ParameterType);
                try
                {
                    object r = m.Invoke(target, vals);
                    return m.Name + "() = " + Summarize(r);
                }
                catch (Exception e) { return "erreur " + e.Message; }
            }
            return "methode " + method + "(" + args.Length + " args) introuvable";
        }

        private static object Walk(object root, string path)
        {
            object cur = root;
            foreach (string part in path.Split('.'))
            {
                if (ReferenceEquals(cur, null)) return null;
                cur = GetMember(cur, cur.GetType(), part);
            }
            return cur;
        }

        private static object WalkStatic(Type t, string path)
        {
            string[] parts = path.Split('.');
            object cur = GetStaticMember(t, parts[0]);
            for (int i = 1; i < parts.Length; i++)
            {
                if (ReferenceEquals(cur, null)) return null;
                cur = GetMember(cur, cur.GetType(), parts[i]);
            }
            return cur;
        }

        private static object WalkParent(object root, string path)
        {
            string[] parts = path.Split('.');
            object cur = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (ReferenceEquals(cur, null)) return null;
                cur = GetMember(cur, cur.GetType(), parts[i]);
            }
            return cur;
        }

        private static string LastMember(string path)
        {
            string[] parts = path.Split('.');
            return parts[parts.Length - 1];
        }

        private static object GetMember(object obj, Type t, string name)
        {
            FieldInfo fi = FindField(t, name);
            if (!ReferenceEquals(fi, null)) return fi.GetValue(obj);
            PropertyInfo pi = FindProp(t, name);
            if (!ReferenceEquals(pi, null) && pi.CanRead && pi.GetIndexParameters().Length == 0) return pi.GetValue(obj, null);
            return null;
        }

        private static object GetStaticMember(Type t, string name)
        {
            FieldInfo fi = FindField(t, name);
            if (!ReferenceEquals(fi, null)) return fi.GetValue(null);
            PropertyInfo pi = FindProp(t, name);
            if (!ReferenceEquals(pi, null) && pi.CanRead && pi.GetIndexParameters().Length == 0) return pi.GetValue(null, null);
            return null;
        }

        private static bool SetMember(object target, Type t, string name, object value)
        {
            FieldInfo fi = FindField(t, name);
            if (!ReferenceEquals(fi, null)) { fi.SetValue(target, value); return true; }
            PropertyInfo pi = FindProp(t, name);
            if (!ReferenceEquals(pi, null) && pi.CanWrite && pi.GetIndexParameters().Length == 0) { pi.SetValue(target, value, null); return true; }
            return false;
        }

        private static Type FindMemberType(object target, Type t, string name)
        {
            FieldInfo fi = FindField(t, name);
            if (!ReferenceEquals(fi, null)) return fi.FieldType;
            PropertyInfo pi = FindProp(t, name);
            if (!ReferenceEquals(pi, null)) return pi.PropertyType;
            return typeof(string);
        }

        private static FieldInfo FindField(Type t, string name)
        {
            for (Type cur = t; !ReferenceEquals(cur, null); cur = cur.BaseType)
            {
                FieldInfo fi = cur.GetField(name, All);
                if (!ReferenceEquals(fi, null)) return fi;
            }
            return null;
        }

        private static PropertyInfo FindProp(Type t, string name)
        {
            for (Type cur = t; !ReferenceEquals(cur, null); cur = cur.BaseType)
            {
                PropertyInfo pi = cur.GetProperty(name, All);
                if (!ReferenceEquals(pi, null)) return pi;
            }
            return null;
        }

        private static Type FindType(string name)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(name);
                if (!ReferenceEquals(t, null)) return t;
            }
            return null;
        }

        private static object ConvertArg(string s, Type t)
        {
            try
            {
                if (ReferenceEquals(t, null) || ReferenceEquals(t, typeof(string))) return s;
                if (ReferenceEquals(t, typeof(int))) return int.Parse(s);
                if (ReferenceEquals(t, typeof(uint))) return uint.Parse(s);
                if (ReferenceEquals(t, typeof(long))) return long.Parse(s);
                if (ReferenceEquals(t, typeof(ulong))) return ulong.Parse(s);
                if (ReferenceEquals(t, typeof(bool))) return bool.Parse(s);
                if (ReferenceEquals(t, typeof(float))) return float.Parse(s);
                if (ReferenceEquals(t, typeof(double))) return double.Parse(s);
                if (t.IsEnum) return Enum.Parse(t, s);
                return Convert.ChangeType(s, t);
            }
            catch { return s; }
        }

        public static string Summarize(object v)
        {
            if (ReferenceEquals(v, null)) return "null";
            if (v is string) return "\"" + v + "\"";
            if (v is IEnumerable en && !(v is string))
            {
                int n = 0;
                StringBuilder sb = new StringBuilder();
                sb.Append(v.GetType().Name).Append('[');
                foreach (object o in en)
                {
                    if (n++ > 0) sb.Append(", ");
                    sb.Append(o == null ? "null" : o.ToString());
                    if (n >= 12) { sb.Append(", ..."); break; }
                }
                sb.Append(']');
                return sb.ToString();
            }
            return v.ToString();
        }
    }
}
