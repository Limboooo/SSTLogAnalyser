using System.Security.Cryptography;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

static class AuditTrunkLogs
{
    public static void Write(string destination)
    {
        var root = GenerateFixtures.Trunk;
        var project = XDocument.Load(Path.Combine(root, "S200VerCalDia.csproj"));
        var files = project.Descendants().Where(n => n.Name.LocalName == "Compile")
            .Select(n => (string?)n.Attribute("Include")).Where(p => p != null)
            .Select(p => Path.GetFullPath(Path.Combine(root, p!))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var entries = new List<object>();
        var counts = new SortedDictionary<string, int>();
        var hashes = new SortedDictionary<string, string>();
        var corpus = new List<string>();
        foreach (var path in files)
        {
            var text = File.ReadAllText(path);
            var tree = CSharpSyntaxTree.ParseText(text);
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            hashes[relative] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            foreach (var call in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var expression = call.Expression.ToString();
                if (call.Expression is MemberBindingExpressionSyntax && call.Parent is ConditionalAccessExpressionSyntax conditional)
                    expression = conditional.Expression + "?" + expression;
                var name = call.Expression is MemberAccessExpressionSyntax member ? member.Name.Identifier.Text : expression;
                var isLog = expression.Contains("log", StringComparison.OrdinalIgnoreCase) &&
                    (name.Contains("log", StringComparison.OrdinalIgnoreCase) || name is "Info" or "Warn" or "Error" or "Fatal" or "Debug" or "DynamicInvoke" or "Invoke" ||
                     new[] { "InfoFormat", "ErrorFormat", "WarnFormat", "FatalFormat", "DebugFormat" }.Contains(name));
                var isFile = name is "Write" or "WriteLine" or "WriteAllText" or "AppendAllText";
                var isBuilder = name is "Append" or "AppendLine" or "AppendFormat" ||
                    name == "Format" && (expression.StartsWith("String.", StringComparison.Ordinal) || expression.StartsWith("string.", StringComparison.Ordinal)) ||
                    name == "Add" && (expression.Contains("sOut", StringComparison.OrdinalIgnoreCase) || expression.Contains("Log", StringComparison.OrdinalIgnoreCase));
                if (!isLog && !isFile && !isBuilder) continue;
                var literals = call.ArgumentList.DescendantNodes().OfType<LiteralExpressionSyntax>()
                    .Where(n => n.IsKind(SyntaxKind.StringLiteralExpression)).Select(n => n.Token.ValueText).ToArray();
                var interpolated = call.ArgumentList.DescendantNodes().OfType<InterpolatedStringExpressionSyntax>().Select(n => n.ToString()).ToArray();
                if (literals.Length > 0)
                {
                    corpus.Add("# Template source: " + relative + ":" + (tree.GetLineSpan(call.Span).StartLinePosition.Line + 1));
                    corpus.Add(literals[0]);
                }
                var method = call.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
                var methodName = method is MethodDeclarationSyntax declaration ? declaration.Identifier.Text : method?.Kind().ToString();
                entries.Add(new {
                    File = relative, Line = tree.GetLineSpan(call.Span).StartLinePosition.Line + 1,
                    Method = methodName, Sink = expression,
                    Category = isBuilder ? "Text construction (review downstream output)" : isFile ? "Text writer (review destination)" : "Log API",
                    Expression = call.ToString(), StringLiterals = literals, InterpolatedStrings = interpolated,
                    Coverage = "All emitted text retained by source-line reader; specialized semantics require a fixture"
                });
                counts[relative] = counts.GetValueOrDefault(relative) + 1;
            }
        }
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.GetFullPath(Path.Combine(destination, "../fixtures/17_Trunk_OutputTemplates.log")), string.Join("\r\n", corpus) + "\r\n");
        File.WriteAllText(Path.Combine(destination, "TRUNK_LOG_CALLS.json"), JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(destination, "TRUNK_SOURCE_HASHES.json"), JsonSerializer.Serialize(hashes, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(destination, "TRUNK_LOG_AUDIT.md"),
            "# Trunk 日志写入点清单\n\n扫描当前 S200VerCalDia.csproj 实际 Compile 列表，使用 C# 语法树提取调用；不扫描 BackUP 或注释。\n\n" +
            $"源文件 {files.Length} 个，候选日志/文本写入/输出文本构造调用 {entries.Count} 个，涉及 {counts.Count} 个文件。\n\n" +
            "| 源文件 | 候选调用数 |\n|---|---:|\n" + string.Join("\n", counts.Select(p => $"| {p.Key} | {p.Value} |")) +
            "\n\nTRUNK_LOG_CALLS.json 保存文件、行号、所在方法、调用表达式和字符串片段。TRUNK_SOURCE_HASHES.json 标记本次工作副本。\n\n" +
            "这是输出点清单，不是全部动态输出格式已完成结构化解析的证明。候选包含 UI-only 日志、文本构造、配置文件写入和条件分支；需结合调用链判断最终输出。循环可在同一调用处产生多种运行时值。禁用的 #if 分支和外部 DLL 输出不在本清单里。\n");
        Console.WriteLine($"Trunk audit: {files.Length} compiled files, {entries.Count} candidate output calls, {counts.Count} emitting files.");
    }
}
