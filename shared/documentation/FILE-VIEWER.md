# CoBrowser local file viewer

## Text and source

The viewer detects text by byte heuristics and common Unicode BOMs. Known language labels include C#, C/C++, Java, Kotlin, JavaScript, TypeScript, JSX/TSX, Python, Go, Rust, Ruby, PHP, Swift, Dart, Lua, PowerShell, shell, SQL, JSON, XML, XAML, YAML, TOML, CSS/SCSS/Less, Vue, Svelte, Astro, GraphQL, Protobuf, Terraform/HCL, R, Objective-C, Perl, Elixir, Erlang, Haskell, Clojure, Scala, F#, Visual Basic, Assembly, Solidity, Prisma, Nix, Zig, Verilog/SystemVerilog, VHDL, Julia, OCaml, PureScript, Reason, ReScript, Scheme, Lisp, Markdown/MDX, CSV/TSV, Diff/Patch and other common source/config formats.

If the extension is unknown but the file is detected as text, it is still displayed as Plain text. This is a reader, not a compiler or full IDE; syntax coloring is intentionally lightweight.

## Images and documents

HTML, XHTML, SVG, PNG, JPEG, GIF, WebP, BMP, ICO, AVIF, APNG, TIFF, PDF and supported audio/video formats are sent to Chromium's native file rendering path. SVG can also be viewed as source using Ctrl+U. Actual format support depends on the Chromium build.

## Binary files

DLL, EXE, SYS, DRV, OCX, COM, shared libraries, PDB, LIB, OBJ, Java class/JAR, WASM and other binary formats are not executed. The read-only inspector shows available file-version/assembly/PE metadata and only the first 1 KiB of bytes in hex/ASCII. It is not a decompiler or disassembler.

## Stability limits

- Maximum input text file size: 24 MiB.
- Maximum rendered preview: 50,000 lines or 2,000,000 characters.
- Long minified lines bypass regex syntax colorization.
- These limits reduce the chance of exhausting renderer memory when opening generated or minified files.
