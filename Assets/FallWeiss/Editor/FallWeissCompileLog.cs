// Editor-only: writes every script compile result to Logs/fallweiss_compile.txt (its own assembly,
// so it still runs when the game scripts fail to compile).
using UnityEditor;
using UnityEditor.Compilation;
using System.IO;
using System.Text;

namespace FallWeissEditor
{
    [InitializeOnLoad]
    static class FallWeissCompileLog
    {
        static readonly string Path = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Logs", "fallweiss_compile.txt");
        static FallWeissCompileLog()
        {
            CompilationPipeline.compilationStarted += _ => { try { File.WriteAllText(Path, "compile started " + System.DateTime.Now + "\n"); } catch { } };
            CompilationPipeline.assemblyCompilationFinished += (asm, msgs) =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("== " + asm);
                foreach (var m in msgs) sb.AppendLine($"{m.type}: {m.file}({m.line},{m.column}): {m.message}");
                try { File.AppendAllText(Path, sb.ToString()); } catch { }
            };
            CompilationPipeline.compilationFinished += _ => { try { File.AppendAllText(Path, "compile finished " + System.DateTime.Now + "\n"); } catch { } };
        }
    }
}
