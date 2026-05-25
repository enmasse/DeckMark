using DeckMark.Core.Model;
using DeckMark.Viewer;

namespace DeckMark.Tests;

public sealed class CodeExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_CSharpCode_ReturnsOutput()
    {
        var executor = new CodeExecutor();
        var block = new ContentBlock
        {
            Kind = BlockKind.CodeBlock,
            Language = "csharp",
            IsExecutable = true,
            RawContent = """
                Console.WriteLine("hej");
                return 42;
                """,
        };

        var result = await executor.ExecuteAsync(block);

        Assert.Equal(ExecutableCodeRunState.Succeeded, result.State);
        Assert.Contains("hej", result.Output);
        Assert.Contains("42", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_CSharpError_ReturnsFailure()
    {
        var executor = new CodeExecutor();
        var block = new ContentBlock
        {
            Kind = BlockKind.CodeBlock,
            Language = "csharp",
            IsExecutable = true,
            RawContent = "throw new Exception(\"boom\");",
        };

        var result = await executor.ExecuteAsync(block);

        Assert.Equal(ExecutableCodeRunState.Failed, result.State);
        Assert.Contains("boom", result.Output);
    }
}
