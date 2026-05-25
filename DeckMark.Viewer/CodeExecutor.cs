using System.Text;
using DeckMark.Core.Model;
using Microsoft.DotNet.Interactive.Commands;
using Microsoft.DotNet.Interactive.CSharp;
using Microsoft.DotNet.Interactive.Events;
using Microsoft.DotNet.Interactive.Formatting;

namespace DeckMark.Viewer;

internal enum ExecutableCodeRunState
{
    Idle,
    Running,
    Succeeded,
    Failed,
}

internal sealed record CodeExecutionResult(ExecutableCodeRunState State, string Output);

internal sealed class CodeExecutor
{
    public async Task<CodeExecutionResult> ExecuteAsync(ContentBlock block, CancellationToken cancellationToken = default)
    {
        if (!block.IsExecutable)
            return new CodeExecutionResult(ExecutableCodeRunState.Failed, "Det här kodblocket är inte markerat som körbart.");

        return NormalizeLanguage(block.Language) switch
        {
            "csharp" => await ExecuteCSharpAsync(block.RawContent, cancellationToken),
            var language => new CodeExecutionResult(
                ExecutableCodeRunState.Failed,
                $"Språket '{language}' stöds inte ännu i viewern. Markera i stället C#-kod som körbar eller lägg till en separat exekveringsadapter."),
        };
    }

    private static async Task<CodeExecutionResult> ExecuteCSharpAsync(string code, CancellationToken cancellationToken)
    {
        using var kernel = new CSharpKernel();
        var result = await kernel.SendAsync(new SubmitCode(code, "csharp"), cancellationToken);
        var output = new StringBuilder();
        var state = ExecutableCodeRunState.Succeeded;

        foreach (var kernelEvent in result.Events)
        {
            switch (kernelEvent)
            {
                case StandardOutputValueProduced standardOutput:
                    AppendFormattedValues(output, standardOutput.FormattedValues);
                    break;
                case StandardErrorValueProduced standardError:
                    state = ExecutableCodeRunState.Failed;
                    AppendFormattedValues(output, standardError.FormattedValues);
                    break;
                case ErrorProduced errorProduced:
                    state = ExecutableCodeRunState.Failed;
                    AppendLine(output, errorProduced.Message);
                    break;
                case CommandFailed commandFailed:
                    state = ExecutableCodeRunState.Failed;
                    AppendLine(output, commandFailed.Message);
                    break;
                case ReturnValueProduced returnValue when returnValue.Value is not null:
                    AppendLine(output, Formatter.ToDisplayString(returnValue.Value, PlainTextFormatter.MimeType));
                    break;
            }
        }

        if (output.Length == 0 && state == ExecutableCodeRunState.Succeeded)
            output.Append("Klar utan output.");

        return new CodeExecutionResult(state, output.ToString().TrimEnd());
    }

    private static void AppendFormattedValues(StringBuilder output, IReadOnlyCollection<FormattedValue> formattedValues)
    {
        foreach (var formattedValue in formattedValues)
        {
            if (string.IsNullOrWhiteSpace(formattedValue.Value))
                continue;

            AppendLine(output, formattedValue.Value);
        }
    }

    private static void AppendLine(StringBuilder output, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (output.Length > 0)
            output.AppendLine();

        output.Append(value.TrimEnd());
    }

    private static string NormalizeLanguage(string? language)
    {
        return language?.Trim().ToLowerInvariant() switch
        {
            "cs" or "c#" => "csharp",
            { Length: > 0 } value => value,
            _ => "text",
        };
    }
}
