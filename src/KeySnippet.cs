using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Microsoft.CSharp;

public static class KeySnippet {
    static readonly object gate = new object();
    static readonly Dictionary<string, Func<string>> cache = new Dictionary<string, Func<string>>(StringComparer.Ordinal);

    public static string Run(string source) {
        if (String.IsNullOrWhiteSpace(source)) return "";
        Func<string> fn;
        lock (gate) {
            if (!cache.TryGetValue(source, out fn)) {
                fn = Compile(source);
                cache[source] = fn;
            }
        }
        return fn == null ? "" : (fn() ?? "");
    }

    static Func<string> Compile(string source) {
        string body = source.Trim();
        if (body.IndexOf("return", StringComparison.Ordinal) < 0)
            body = "return (" + body.Trim().TrimEnd(';') + ");";

        string code =
            "using System;\nusing System.Globalization;\nusing System.IO;\n" +
            "public static class KeySnippetHost {\n" +
            "  public static string Run() {\n    " + body + "\n  }\n}";

        var provider = new CSharpCodeProvider();
        var parms = new CompilerParameters();
        parms.GenerateInMemory = true;
        parms.GenerateExecutable = false;
        parms.ReferencedAssemblies.Add("System.dll");
        parms.ReferencedAssemblies.Add("System.Core.dll");
        CompilerResults results = provider.CompileAssemblyFromSource(parms, code);
        if (results.Errors.HasErrors) {
            var sb = new StringBuilder();
            foreach (CompilerError err in results.Errors) {
                if (!err.IsWarning) sb.AppendLine(err.ErrorText);
            }
            throw new InvalidOperationException(sb.ToString().Trim());
        }
        MethodInfo method = results.CompiledAssembly.GetType("KeySnippetHost").GetMethod("Run");
        return (Func<string>)Delegate.CreateDelegate(typeof(Func<string>), method);
    }
}
