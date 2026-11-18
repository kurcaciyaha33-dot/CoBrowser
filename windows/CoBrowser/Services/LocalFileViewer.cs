using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.Diagnostics;
using System.Reflection;

namespace CoBrowser.Services;

/// <summary>Routes local files to Chromium, a safe text/code viewer, or a read-only binary inspector.</summary>
public static class LocalFileViewer
{
    private const long MaxTextFileBytes = 24L * 1024 * 1024;
    private const int MaxPreviewCharacters = 2_000_000;
    private const int MaxPreviewLines = 50_000;
    private const int BinaryPreviewBytes = 1024;

    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".xaml", ".xml", ".json", ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx",
        ".py", ".java", ".kt", ".kts", ".c", ".h", ".cc", ".cpp", ".cxx", ".hpp", ".hh",
        ".rs", ".go", ".rb", ".php", ".swift", ".dart", ".lua", ".ps1", ".psm1", ".psd1",
        ".sh", ".bash", ".zsh", ".bat", ".cmd", ".sql", ".yaml", ".yml", ".toml", ".ini",
        ".conf", ".config", ".properties", ".gradle", ".kts", ".md", ".markdown", ".txt", ".log",
        ".css", ".scss", ".less", ".vue", ".svelte", ".astro", ".graphql", ".gql", ".proto",
        ".make", ".cmake", ".dockerfile", ".tf", ".hcl", ".r", ".m", ".mm", ".pl", ".ex", ".exs",
        ".erl", ".hrl", ".hs", ".lhs", ".elm", ".clj", ".cljs", ".scala", ".sc", ".fs", ".fsx",
        ".vb", ".vbs", ".asm", ".s", ".wasm.txt", ".csv", ".tsv", ".mdx", ".diff", ".patch",
        ".lock", ".nix", ".zig", ".sol", ".prisma", ".ipynb", ".tex", ".bib", ".v", ".sv",
        ".vhd", ".vhdl", ".jl", ".rkt", ".scm", ".ss", ".lisp", ".cl", ".cljc", ".edn",
        ".pl", ".pm", ".pp", ".purs", ".re", ".rei", ".res", ".resi", ".ml", ".mli",
        ".solidity", ".gradle.kts", ".properties", ".service", ".desktop", ".env.example"
    };

    private static readonly HashSet<string> BrowserRenderedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".xhtml", ".svg", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
        ".ico", ".avif", ".apng", ".tif", ".tiff", ".pdf", ".mp4", ".webm", ".ogg", ".mp3",
        ".wav", ".m4a", ".mov"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".sys", ".drv", ".ocx", ".com", ".so", ".dylib", ".pdb", ".lib", ".obj",
        ".a", ".class", ".dex", ".apk", ".aab", ".jar", ".bin", ".dat", ".pak", ".wasm"
    };

    public static string CreateNavigationTarget(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("File was not found.", fullPath);

        var extension = Path.GetExtension(fullPath);
        var fileName = Path.GetFileName(fullPath);

        // These are displayed by Chromium itself (SVG/PNG included), rather than being treated as source text.
        if (BrowserRenderedExtensions.Contains(extension))
            return new Uri(fullPath).AbsoluteUri;

        if (BinaryExtensions.Contains(extension))
            return CreateBinaryViewer(fullPath);

        var info = new FileInfo(fullPath);
        var looksTextual = IsTextFile(fullPath);
        if (looksTextual)
        {
            if (info.Length <= MaxTextFileBytes) return CreateCodeViewer(fullPath);
            return CreateNoticePage(fileName, $"This text file is {FormatBytes(info.Length)}. The built-in viewer is limited to {FormatBytes(MaxTextFileBytes)} for stability. Open it with a code editor or split it into smaller files.");
        }

        // Unknown binary formats are inspected safely instead of being treated as source code.
        return CreateBinaryViewer(fullPath);
    }

    private static bool IsTextFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var sample = new byte[Math.Min(4096, (int)Math.Min(stream.Length, 4096))];
            var read = stream.Read(sample, 0, sample.Length);
            if (read == 0) return true;
            if (read >= 2 && ((sample[0] == 0xFF && sample[1] == 0xFE) || (sample[0] == 0xFE && sample[1] == 0xFF))) return true;
            if (read >= 4 && ((sample[0] == 0x00 && sample[1] == 0x00 && sample[2] == 0xFE && sample[3] == 0xFF) ||
                              (sample[0] == 0xFF && sample[1] == 0xFE && sample[2] == 0x00 && sample[3] == 0x00))) return true;
            var controls = 0;
            for (var i = 0; i < read; i++)
            {
                var b = sample[i];
                if (b == 0) return false;
                if (b < 9 || (b > 13 && b < 32)) controls++;
            }
            return controls < Math.Max(2, read / 50);
        }
        catch (Exception ex)
        {
            App.LogException(ex, "Detect local text file");
            return false;
        }
    }

    private static string CreateCodeViewer(string sourcePath) => CreateTextSnapshot(sourcePath, ReadText(sourcePath));

    public static string CreateSourceSnapshot(string sourcePath)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Source file was not found.", sourcePath);
        var info = new FileInfo(sourcePath);
        if (!IsTextFile(sourcePath)) return CreateBinaryViewer(sourcePath);
        if (info.Length > MaxTextFileBytes)
            return CreateNoticePage(info.Name, $"This text file is {FormatBytes(info.Length)}. The built-in viewer is limited to {FormatBytes(MaxTextFileBytes)} to protect browser memory.");
        return CreateTextSnapshot(sourcePath, ReadText(sourcePath));
    }

    public static string CreateTextSnapshot(string sourcePath, string source)
    {
        if (source.Length > MaxTextFileBytes)
            return CreateNoticePage(Path.GetFileName(sourcePath), $"This text contains more than {FormatBytes(MaxTextFileBytes)} of data. The built-in viewer has a safety limit to protect browser memory.");

        var extension = Path.GetExtension(sourcePath);
        var language = GetLanguageName(Path.GetFileName(sourcePath), extension);
        var lineMarkup = new StringBuilder(Math.Min(source.Length * 2, 4 * 1024 * 1024));
        var lineCount = 0;
        var renderedCharacters = 0;
        var truncated = false;

        // Do not Split() a large source into millions of strings. Render a bounded number of lines
        // and characters so a minified file or generated source cannot exhaust the CEF renderer.
        using (var reader = new StringReader(source))
        {
            while (reader.ReadLine() is { } line)
            {
                if (lineCount >= MaxPreviewLines || renderedCharacters >= MaxPreviewCharacters)
                {
                    truncated = true;
                    break;
                }

                var remaining = MaxPreviewCharacters - renderedCharacters;
                if (line.Length > remaining)
                {
                    line = line[..remaining];
                    truncated = true;
                }

                lineMarkup.Append("<div class=\"line\"><span class=\"ln\">")
                    .Append(lineCount + 1)
                    .Append("</span><code>")
                    .Append(HighlightLine(line))
                    .Append("</code></div>");

                renderedCharacters += line.Length;
                lineCount++;
                if (truncated) break;
            }
        }

        var truncationNotice = truncated
            ? "<div class=\"limit\">Preview limited to 50,000 lines / 2,000,000 characters to keep Chromium responsive. The original file is unchanged.</div>"
            : string.Empty;

        var html = $$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{{WebUtility.HtmlEncode(Path.GetFileName(sourcePath))}} · CoBrowser Code Viewer</title>
            <style>
              :root{color-scheme:dark;--bg:#101722;--panel:#182234;--line:#2a3a51;--fg:#e7eefb;--muted:#8e9bb0;--blue:#79bbff;--green:#9bd7b1;--purple:#c7a8ff;--yellow:#f6d58c}
              *{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.55 "Segoe UI",system-ui,sans-serif}
              header{position:sticky;top:0;z-index:2;background:rgba(24,34,52,.97);backdrop-filter:blur(14px);border-bottom:1px solid var(--line);padding:15px 22px}
              .top{display:flex;gap:14px;align-items:center;flex-wrap:wrap}.brand{font-weight:750;color:var(--blue);letter-spacing:.2px}.name{font-size:16px;font-weight:650;overflow-wrap:anywhere}
              .meta{color:var(--muted);font-size:12px;margin-top:4px}.pill{border:1px solid #40516d;border-radius:999px;padding:4px 10px;color:#b9d7ff;font-size:11px;text-transform:uppercase;letter-spacing:.08em}
              input{margin-top:12px;width:min(440px,100%);background:#101722;border:1px solid #3b4b65;border-radius:10px;padding:9px 12px;color:var(--fg);outline:none}input:focus{border-color:var(--blue)}
              main{padding:16px 0 42px;overflow:auto}.code{min-width:max-content;font:13px/1.65 Consolas,"Cascadia Code",monospace;white-space:pre;tab-size:4}
              .line{display:flex;min-height:21px;padding:0 18px}.line:hover{background:#1b2637}.ln{display:inline-block;width:58px;flex:none;text-align:right;padding-right:18px;color:#66758c;user-select:none;border-right:1px solid #243044;margin-right:18px}
              code{color:#dbe7f8}.kw{color:var(--purple)}.str{color:var(--green)}.num{color:var(--yellow)}.comment{color:#7f91a9;font-style:italic}
              .limit{margin:14px 22px;padding:12px 15px;border:1px solid #6a5735;border-radius:10px;background:#292315;color:#f4d99b;font-size:12px}
              footer{position:fixed;bottom:0;right:0;padding:8px 15px;color:var(--muted);background:rgba(16,23,34,.94);font-size:11px}
            </style></head><body>
            <header><div class="top"><span class="brand">CoBrowser / Code</span><span class="name">{{WebUtility.HtmlEncode(Path.GetFileName(sourcePath))}}</span><span class="pill">{{WebUtility.HtmlEncode(language)}}</span></div>
              <div class="meta">{{WebUtility.HtmlEncode(sourcePath)}} · {{FormatBytes(new FileInfo(sourcePath).Length)}} · {{lineCount:N0}} lines shown</div>
              <input id="filter" type="search" placeholder="Find text in this file…" autocomplete="off">
            </header><main class="code" id="code">{{lineMarkup}}{{truncationNotice}}</main><footer>Read-only local file · CoBrowser</footer>
            <script>
              const f=document.getElementById('filter');const rows=[...document.querySelectorAll('.line')];
              f.addEventListener('input',()=>{const q=f.value.toLowerCase();for(const r of rows)r.style.display=r.textContent.toLowerCase().includes(q)?'flex':'none';});
            </script></body></html>
            """;

        return WriteViewerFile(html, "code-view");
    }

    private static string CreateBinaryViewer(string sourcePath)
    {
        var info = new FileInfo(sourcePath);
        var data = new byte[Math.Min(BinaryPreviewBytes, (int)Math.Min(info.Length, BinaryPreviewBytes))];
        using (var stream = File.OpenRead(sourcePath)) _ = stream.Read(data, 0, data.Length);

        var hex = new StringBuilder();
        for (var offset = 0; offset < data.Length; offset += 16)
        {
            var count = Math.Min(16, data.Length - offset);
            hex.Append(offset.ToString("X8")).Append("  ");
            for (var i = 0; i < 16; i++)
                hex.Append(i < count ? data[offset + i].ToString("X2") : "  ").Append(i == 7 ? "  " : " ");
            hex.Append(" | ");
            for (var i = 0; i < count; i++)
                hex.Append(data[offset + i] is >= 32 and <= 126 ? (char)data[offset + i] : '.');
            hex.AppendLine();
        }

        var signature = data.Length >= 2 && data[0] == 0x4D && data[1] == 0x5A ? "Windows PE/DOS executable signature (MZ)" : "Binary file";
        if (data.Length >= 64 && data[0] == 0x4D && data[1] == 0x5A)
        {
            try
            {
                var peOffset = BitConverter.ToInt32(data, 0x3C);
                using var peStream = File.OpenRead(sourcePath);
                if (peOffset >= 0 && peOffset <= peStream.Length - 24)
                {
                    peStream.Position = peOffset;
                    using var reader = new BinaryReader(peStream, Encoding.UTF8, leaveOpen: true);
                    var peSignature = reader.ReadUInt32();
                    if (peSignature == 0x00004550)
                    {
                        var machine = reader.ReadUInt16();
                        var sections = reader.ReadUInt16();
                        _ = reader.ReadUInt32(); // timestamp
                        _ = reader.ReadUInt32(); // symbol table pointer
                        _ = reader.ReadUInt32(); // symbol count
                        var optionalHeaderSize = reader.ReadUInt16();
                        var characteristics = reader.ReadUInt16();
                        signature = "Valid Windows PE image" + Environment.NewLine +
                            "Architecture: " + GetPeMachineName(machine) + Environment.NewLine +
                            "Sections: " + sections + Environment.NewLine +
                            "Optional header: " + optionalHeaderSize + " bytes" + Environment.NewLine +
                            "Characteristics: 0x" + characteristics.ToString("X4");
                    }
                    else signature = "MZ image with an invalid PE signature";
                }
                else signature = "MZ image; PE header offset is invalid or outside the file";
            }
            catch (Exception ex)
            {
                signature = "MZ executable signature; PE metadata could not be read";
                App.LogException(ex, "Read PE metadata");
            }
        }

        var metadata = new StringBuilder();
        metadata.AppendLine(signature).AppendLine("File size: " + FormatBytes(info.Length));
        try
        {
            var fileVersion = FileVersionInfo.GetVersionInfo(sourcePath);
            if (!string.IsNullOrWhiteSpace(fileVersion.FileDescription)) metadata.AppendLine("Description: " + fileVersion.FileDescription);
            if (!string.IsNullOrWhiteSpace(fileVersion.FileVersion)) metadata.AppendLine("File version: " + fileVersion.FileVersion);
            if (!string.IsNullOrWhiteSpace(fileVersion.ProductName)) metadata.AppendLine("Product: " + fileVersion.ProductName);
            if (!string.IsNullOrWhiteSpace(fileVersion.ProductVersion)) metadata.AppendLine("Product version: " + fileVersion.ProductVersion);
            if (!string.IsNullOrWhiteSpace(fileVersion.CompanyName)) metadata.AppendLine("Company: " + fileVersion.CompanyName);
            if (!string.IsNullOrWhiteSpace(fileVersion.OriginalFilename)) metadata.AppendLine("Original filename: " + fileVersion.OriginalFilename);
            if (!string.IsNullOrWhiteSpace(fileVersion.LegalCopyright)) metadata.AppendLine("Copyright: " + fileVersion.LegalCopyright);
        }
        catch (Exception ex) { App.LogException(ex, "Read binary version metadata"); }
        try
        {
            var assemblyName = AssemblyName.GetAssemblyName(sourcePath);
            metadata.AppendLine("Managed .NET assembly: " + assemblyName.Name)
                .AppendLine("Assembly version: " + assemblyName.Version);
        }
        catch (BadImageFormatException) { metadata.AppendLine("Managed .NET assembly metadata: not detected (may be a native DLL or another binary format)"); }
        catch (Exception ex) { App.LogException(ex, "Read .NET assembly metadata"); }

        var html = $$"""
            <!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>{{WebUtility.HtmlEncode(Path.GetFileName(sourcePath))}} · CoBrowser Binary Inspector</title>
            <style>
              *{box-sizing:border-box}body{margin:0;background:#111722;color:#e7eefb;font:14px/1.6 "Segoe UI",system-ui,sans-serif}
              header{padding:24px;background:#1a2332;border-bottom:1px solid #2a3548}.brand{color:#79bbff;font-weight:700}.name{font-size:21px;font-weight:650;margin:8px 0}.muted{color:#8e9bb0;font-size:12px}
              .wrap{padding:24px;max-width:1100px}.notice{background:#2a2630;border:1px solid #62506a;color:#e8d6f5;padding:14px 16px;border-radius:12px;margin-bottom:18px}
              .card{background:#1a2332;border:1px solid #2a3548;border-radius:13px;padding:16px;margin:12px 0}pre{overflow:auto;background:#0b111b;border:1px solid #2a3548;border-radius:10px;padding:16px;color:#b9d7ff;font:13px/1.6 Consolas,monospace}
            </style></head><body><header><div class="brand">CoBrowser / Binary Inspector</div><div class="name">{{WebUtility.HtmlEncode(Path.GetFileName(sourcePath))}}</div><div class="muted">{{WebUtility.HtmlEncode(sourcePath)}} · {{FormatBytes(info.Length)}}</div></header>
            <div class="wrap"><div class="notice">This is a read-only binary inspection. CoBrowser will not execute, load, or decompile this file. A DLL is a compiled binary; use a trusted development tool for full metadata or disassembly.</div>
            <div class="card"><b>File metadata</b><pre>{{WebUtility.HtmlEncode(metadata.ToString())}}</pre><span class="muted">Showing the first {{data.Length}} bytes only in the hex preview.</span></div>
            <div class="card"><b>Hex / ASCII preview</b><pre>{{WebUtility.HtmlEncode(hex.ToString())}}</pre></div></div></body></html>
            """;

        return WriteViewerFile(html, "binary-view");
    }

    private static string CreateNoticePage(string title, string message)
    {
        var html = $$"""
            <!doctype html><html><head><meta charset="utf-8"><title>{{WebUtility.HtmlEncode(title)}} · CoBrowser</title>
            <style>body{font:15px/1.6 Segoe UI, sans-serif;background:#111722;color:#e7eefb;margin:0;padding:8vh 10vw}.card{max-width:720px;background:#1a2332;border:1px solid #35435b;border-radius:18px;padding:28px}.brand{color:#79bbff;font-weight:700}</style>
            </head><body><div class="card"><div class="brand">CoBrowser File Viewer</div><h1>{{WebUtility.HtmlEncode(title)}}</h1><p>{{WebUtility.HtmlEncode(message)}}</p></div></body></html>
            """;
        return WriteViewerFile(html, "file-notice");
    }

    private static string GetPeMachineName(ushort machine) => machine switch
    {
        0x014c => "x86 (I386)",
        0x8664 => "x64 (AMD64)",
        0xAA64 => "ARM64",
        0x01c0 or 0x01c4 => "ARM",
        0x0200 => "Intel Itanium",
        _ => $"Unknown (0x{machine:X4})"
    };

    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        // Respect UTF BOMs first; otherwise prefer strict UTF-8 and fall back to Latin-1 rather than
        // inserting replacement characters into legacy single-byte source files.
        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            return new UTF32Encoding(true, false).GetString(bytes, 4, bytes.Length - 4);
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
            return Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    private static string HighlightLine(string line)
    {
        // Very long minified/generated lines are displayed without regex tokenization to avoid
        // excessive regex work and protect the renderer from pathological source files.
        if (line.Length > 20_000) return WebUtility.HtmlEncode(line);
        var safe = WebUtility.HtmlEncode(line);
        safe = Regex.Replace(safe, "(&quot;.*?&quot;|'(?:\\\\.|[^'])*'|`.*?`)", "<span class=\"str\">$1</span>");
        safe = Regex.Replace(safe, @"\b(?:abstract|async|await|bool|boolean|break|byte|case|catch|char|class|const|continue|def|default|do|double|else|enum|export|extends|false|final|finally|float|for|foreach|from|function|if|implements|import|in|int|interface|let|long|namespace|new|null|object|package|private|protected|public|return|self|static|string|struct|switch|this|throw|throws|true|try|using|var|void|while|yield|SELECT|FROM|WHERE|INSERT|UPDATE|DELETE|CREATE|TABLE|JOIN|AND|OR|NOT)\b",
            "<span class=\"kw\">$0</span>", RegexOptions.IgnoreCase);
        safe = Regex.Replace(safe, @"\b\d+(?:\.\d+)?\b", "<span class=\"num\">$0</span>");
        return safe.Length == 0 ? "&nbsp;" : safe;
    }

    private static string GetLanguageName(string fileName, string extension)
    {
        if (fileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)) return "Dockerfile";
        if (fileName.Equals("Makefile", StringComparison.OrdinalIgnoreCase)) return "Makefile";
        if (fileName.Equals(".env", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)) return "Environment";
        if (fileName.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase) || fileName.Equals("Containerfile", StringComparison.OrdinalIgnoreCase)) return "Dockerfile";
        if (fileName.Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase)) return "CMake";
        if (fileName.Equals("Jenkinsfile", StringComparison.OrdinalIgnoreCase)) return "Jenkinsfile";
        if (fileName.Equals("Vagrantfile", StringComparison.OrdinalIgnoreCase)) return "Vagrantfile";
        if (fileName.Equals("Gemfile", StringComparison.OrdinalIgnoreCase)) return "Ruby / Bundler";
        if (fileName.Equals("Rakefile", StringComparison.OrdinalIgnoreCase)) return "Ruby / Rake";
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "C#", ".xaml" => "XAML", ".xml" => "XML", ".svg" => "SVG / XML", ".html" or ".htm" => "HTML", ".xhtml" => "XHTML",
            ".json" => "JSON", ".js" or ".mjs" or ".cjs" => "JavaScript",
            ".ts" or ".tsx" => "TypeScript", ".jsx" => "JSX", ".py" => "Python", ".java" => "Java", ".kt" or ".kts" => "Kotlin",
            ".c" or ".h" => "C", ".cc" or ".cpp" or ".cxx" or ".hpp" or ".hh" => "C++", ".rs" => "Rust", ".go" => "Go",
            ".rb" => "Ruby", ".php" => "PHP", ".swift" => "Swift", ".dart" => "Dart", ".lua" => "Lua", ".ps1" or ".psm1" or ".psd1" => "PowerShell",
            ".sh" or ".bash" or ".zsh" => "Shell", ".bat" or ".cmd" => "Batch", ".sql" => "SQL", ".yaml" or ".yml" => "YAML",
            ".toml" => "TOML", ".ini" or ".conf" => "Config", ".properties" => "Properties", ".gradle" => "Gradle", ".md" or ".markdown" => "Markdown",
            ".css" => "CSS", ".scss" => "SCSS", ".less" => "Less", ".vue" => "Vue", ".svelte" => "Svelte", ".astro" => "Astro",
            ".graphql" or ".gql" => "GraphQL", ".proto" => "Protocol Buffers", ".tf" => "Terraform", ".hcl" => "HCL", ".r" => "R",
            ".m" or ".mm" => "Objective-C", ".pl" => "Perl", ".ex" or ".exs" => "Elixir", ".erl" or ".hrl" => "Erlang", ".hs" or ".lhs" => "Haskell",
            ".clj" or ".cljs" => "Clojure", ".scala" or ".sc" => "Scala", ".fs" or ".fsx" => "F#", ".vb" or ".vbs" => "Visual Basic",
            ".asm" or ".s" => "Assembly", ".csv" => "CSV", ".tsv" => "TSV", ".txt" => "Text", ".log" => "Log",
            ".mdx" => "MDX", ".diff" or ".patch" => "Diff", ".nix" => "Nix", ".zig" => "Zig", ".sol" or ".solidity" => "Solidity",
            ".prisma" => "Prisma", ".tex" => "LaTeX", ".bib" => "BibTeX", ".v" or ".sv" => "Verilog / SystemVerilog",
            ".vhd" or ".vhdl" => "VHDL", ".jl" => "Julia", ".rkt" => "Racket", ".scm" or ".ss" => "Scheme",
            ".lisp" or ".cl" => "Lisp / OpenCL", ".cljc" or ".edn" => "Clojure", ".pp" => "Puppet",
            ".purs" => "PureScript", ".re" or ".rei" => "Reason", ".res" or ".resi" => "ReScript",
            ".ml" or ".mli" => "OCaml", ".desktop" or ".service" => "Desktop / Service config", ".ipynb" => "Jupyter Notebook (JSON)",
            _ => CodeExtensions.Contains(extension) && !string.IsNullOrWhiteSpace(extension)
                ? extension.TrimStart('.').ToUpperInvariant() + " source"
                : "Plain text"
        };
    }

    private static string WriteViewerFile(string html, string prefix)
    {
        var folder = Path.Combine(App.DataRoot, "Viewer");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, $"{prefix}-{Guid.NewGuid():N}.html");
        File.WriteAllText(target, html, new UTF8Encoding(false));
        return new Uri(target).AbsoluteUri;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }
}
