namespace auvdisk.Dbg;
#if DEBUG
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;
using PrettyPrompt;
using PrettyPrompt.Consoles;
using PrettyPrompt.Highlighting;
using CharacterSetModificationKind = PrettyPrompt.Consoles.CharacterSetModificationKind;
using CharacterSetModificationRule = PrettyPrompt.Consoles.CharacterSetModificationRule;
using TextSpan = PrettyPrompt.Documents.TextSpan;

public class ReplCallbacks(CSharpRepl repl) : PromptCallbacks
{
    protected override Task<bool> ShouldOpenCompletionWindowAsync(string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        var textBeforeCaret = text.Substring(0, caret);
        var lastWord = repl.GetLastPartialWord(textBeforeCaret);

        if (lastWord.Length >= 3 && caret == text.Length - 1)
        {
            return Task.FromResult(true);
        }
        
        return base.ShouldOpenCompletionWindowAsync(text, caret, keyPress, cancellationToken);
    }

    protected override Task<IReadOnlyCollection<FormatSpan>> HighlightCallbackAsync(string text, CancellationToken cancellationToken)
    {
        // TODO: do async properly?
        return Task.FromResult(repl.HighlightCode(text));
    }

    protected override Task<IReadOnlyList<PrettyPrompt.Completion.CompletionItem>> GetCompletionItemsAsync(string text, int caret, TextSpan spanToBeReplaced, CancellationToken cancellationToken)
    {
        // TODO: spanToBeReplaced handling
        return repl.GetCompletionsAsync(text, caret).ContinueWith(result =>
        {
            IReadOnlyList<PrettyPrompt.Completion.CompletionItem> returnValue = result.Result.Select(x => new PrettyPrompt.Completion.CompletionItem(
                replacementText: x.DisplayText,
                displayText: new FormattedString(x.DisplayText),
                commitCharacterRules: [new CharacterSetModificationRule(CharacterSetModificationKind.Add, [' '])],
                getExtendedDescription: _ => Task.FromResult(new FormattedString(x.InlineDescription))
            )).ToList();
            
            return returnValue;
        }, cancellationToken);
    }
}

public class CSharpRepl
{
    private ScriptState<object>? _scriptState;
    private readonly ScriptOptions _scriptOptions;
    private readonly AdhocWorkspace _workspace;
    private readonly List<string> _context;

    public static void EntryPoint()
    {
        Console.WriteLine("C# REPL - Type 'exit' to quit");
        Console.WriteLine("==========================================\n");

        var repl = new CSharpRepl();
        repl.RunAsync().GetAwaiter().GetResult();
    }
    
    private CSharpRepl()
    {
        _context = new List<string>();
        _workspace = new AdhocWorkspace();
        
        _scriptOptions = ScriptOptions.Default
            .AddReferences(
                typeof(object).Assembly,
                typeof(Enumerable).Assembly,
                typeof(Console).Assembly,
                typeof(System.IO.File).Assembly,
                System.Reflection.Assembly.GetEntryAssembly()!
            )
            .AddImports(
                "System",
                "System.Linq",
                "System.Collections.Generic",
                "System.IO",
                "System.Text",
                "System.Threading.Tasks"
            );
    }
    
    internal string GetLastPartialWord(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        int i = text.Length - 1;
        while (i >= 0 && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
            i--;

        return text.Substring(i + 1);
    }

    public async Task RunAsync()
    {
        var promptCallbacks = new ReplCallbacks(this);

        var historyLocation = Path.Join(Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? ".", "repl_history");
        
        var prompt = new Prompt(
            persistentHistoryFilepath: historyLocation,
            callbacks: promptCallbacks,
            configuration: new PromptConfiguration(
                prompt: new FormattedString(">>> ", new FormatSpan(0, 4, new ConsoleFormat(Foreground: AnsiColor.Green))),
                keyBindings: new KeyBindings()
            )
        );

        while (true)
        {
            var response = await prompt.ReadLineAsync();
            
            if (!response.IsSuccess)
                break;

            var input = response.Text.Trim();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                break;

            if (input.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                Console.Clear();
                continue;
            }

            if (input.Equals("context", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < _context.Count; i++)
                    Console.WriteLine($"{i + 1}: {_context[i]}");
                continue;
            }

            try
            {
                // Execute the script
                if (_scriptState == null)
                {
                    _scriptState = await CSharpScript.RunAsync(input, _scriptOptions);
                }
                else
                {
                    _scriptState = await _scriptState.ContinueWithAsync(input);
                }

                // Display result if there is one
                if (_scriptState.ReturnValue != null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"=> {FormatValue(_scriptState.ReturnValue)}");
                    Console.ResetColor();
                }
                
                _context.Add(input);
            }
            catch (CompilationErrorException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Compilation error: {ex.Message}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Runtime error: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine();
        }
    }

    public async Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(string text, int caret)
    {
        try
        {
            // TODO: tidy up and move most of the things out of here. document.WithText can be used to update document contents
            // Build the full context: previous submissions + current input
            var fullContext = string.Join(Environment.NewLine, _context);
            if (!string.IsNullOrEmpty(fullContext))
            {
                fullContext += Environment.NewLine;
            }
            fullContext += text;
            
            // Adjust caret position for the full context
            var adjustedCaret = fullContext.Length - text.Length + caret;
            
            // Create a source text from the input
            var sourceText = SourceText.From(fullContext);

            // Create a script document
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);
            
            var compilationOptions = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                usings: _scriptOptions.Imports
            );

            // Convert script options references to proper metadata references
            var metadataReferences = _scriptOptions.MetadataReferences
                .OfType<PortableExecutableReference>()
                .ToList();

            var parseOptions = CSharpParseOptions.Default.WithKind(SourceCodeKind.Script);
            
            var projectInfo = ProjectInfo.Create(
                projectId,
                VersionStamp.Default,
                "Script",
                "Script",
                LanguageNames.CSharp,
                isSubmission: true,
                compilationOptions: compilationOptions,
                metadataReferences: metadataReferences,
                parseOptions: parseOptions
            );
            
            var project = _workspace.AddProject(projectInfo);
            
            var scriptDocumentInfo = DocumentInfo.Create(
                DocumentId.CreateNewId(project.Id), "Script",
                sourceCodeKind: SourceCodeKind.Script,
                loader: TextLoader.From(TextAndVersion.Create(sourceText, VersionStamp.Create())));

            // Get completion service
            var document = _workspace.AddDocument(scriptDocumentInfo);
            
            var completionService = CompletionService.GetService(document);
            if (completionService == null)
                return Array.Empty<CompletionItem>();

            // Get completions at caret position
            var completions = await completionService.GetCompletionsAsync(document, adjustedCaret);
            
            if (completions.ItemsList.Count == 0)
                return Array.Empty<CompletionItem>();

            var textBeforeCaret = text.Substring(0, caret);
            var lastWord = GetLastPartialWord(textBeforeCaret);
            
            var items = completions.ItemsList
                .Select(item => new
                {
                    Item = item,
                    // Calculate match quality
                    StartsWithExact = item.DisplayText.StartsWith(lastWord, StringComparison.Ordinal),
                    StartsWithIgnoreCase = item.DisplayText.StartsWith(lastWord, StringComparison.OrdinalIgnoreCase),
                    ContainsIgnoreCase = item.DisplayText.IndexOf(lastWord, StringComparison.OrdinalIgnoreCase) >= 0,
                    // Use Roslyn's rules for sorting
                    MatchPriority = item.Rules.MatchPriority,
                    // Prefer shorter names
                    Length = item.DisplayText.Length
                })
                .Where(x => string.IsNullOrEmpty(lastWord) || x.ContainsIgnoreCase)
                .OrderByDescending(x => x.StartsWithExact)
                .ThenByDescending(x => x.StartsWithIgnoreCase)
                .ThenByDescending(x => x.MatchPriority)
                .ThenBy(x => x.Length)
                .ThenBy(x => x.Item.DisplayText)
                .Select(x => x.Item)
                .ToList();

            // Clean up workspace
            _workspace.ClearSolution();

            return items;
        }
        catch
        {
            return Array.Empty<CompletionItem>();
        }
    }

    public IReadOnlyCollection<FormatSpan> HighlightCode(string text)
    {
        try
        {
            var spans = new List<FormatSpan>();
            var syntaxTree = CSharpSyntaxTree.ParseText(text, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
            var root = syntaxTree.GetRoot();

            foreach (var token in root.DescendantTokens())
            {
                var span = token.Span;
                AnsiColor? color = token.Kind() switch
                {
                    SyntaxKind.PublicKeyword or SyntaxKind.PrivateKeyword or 
                    SyntaxKind.ProtectedKeyword or SyntaxKind.InternalKeyword or
                    SyntaxKind.StaticKeyword or SyntaxKind.VirtualKeyword or
                    SyntaxKind.AbstractKeyword or SyntaxKind.OverrideKeyword or
                    SyntaxKind.ClassKeyword or SyntaxKind.InterfaceKeyword or
                    SyntaxKind.StructKeyword or SyntaxKind.EnumKeyword or
                    SyntaxKind.NamespaceKeyword or SyntaxKind.UsingKeyword or
                    SyntaxKind.NewKeyword or SyntaxKind.VarKeyword or
                    SyntaxKind.ReturnKeyword or SyntaxKind.IfKeyword or
                    SyntaxKind.ElseKeyword or SyntaxKind.WhileKeyword or
                    SyntaxKind.ForKeyword or SyntaxKind.ForEachKeyword or
                    SyntaxKind.TryKeyword or SyntaxKind.CatchKeyword or
                    SyntaxKind.FinallyKeyword or SyntaxKind.ThrowKeyword or
                    SyntaxKind.AsyncKeyword or SyntaxKind.AwaitKeyword
                        => AnsiColor.Blue,

                    SyntaxKind.StringLiteralToken 
                        => AnsiColor.BrightYellow,

                    SyntaxKind.NumericLiteralToken 
                        => AnsiColor.Cyan,

                    SyntaxKind.TrueKeyword or SyntaxKind.FalseKeyword or SyntaxKind.NullKeyword
                        => AnsiColor.Magenta,

                    _ => null
                };

                // Highlight comments
                if (token.HasLeadingTrivia)
                {
                    foreach (var trivia in token.LeadingTrivia)
                    {
                        if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || 
                            trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
                        {
                            spans.Add(new FormatSpan(
                                trivia.Span.Start,
                                trivia.Span.Length,
                                new ConsoleFormat(Foreground: AnsiColor.Green)
                            ));
                        }
                    }
                }

                if (color.HasValue)
                {
                    spans.Add(new FormatSpan(
                        span.Start,
                        span.Length,
                        new ConsoleFormat(Foreground: color.Value)
                    ));
                }
            }

            return spans;
        }
        catch
        {
            return Array.Empty<FormatSpan>();
        }
    }

    private string FormatValue(object? value)
    {
        if (value == null)
            return "null";

        if (value is string str)
            return $"\"{str}\"";

        if (value is System.Collections.IEnumerable enumerable and not string)
        {
            var items = enumerable.Cast<object>().Take(100).Select(FormatValue);
            return $"[{string.Join(", ", items)}]";
        }

        return value.ToString() ?? "null";
    }
}
#endif