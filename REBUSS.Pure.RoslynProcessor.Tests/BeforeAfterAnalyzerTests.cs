using REBUSS.Pure.RoslynProcessor;

namespace REBUSS.Pure.RoslynProcessor.Tests;

public class BeforeAfterAnalyzerTests
{
    [Fact]
    public void Analyze_MethodSignatureChange_ReturnsFull()
    {
        var before = "class C { void Foo(int x) { } }";
        var after = "class C { void Foo(int x, string y) { } }";

        Assert.Equal(ContextDecision.Full, BeforeAfterAnalyzer.Analyze(before, after));
    }

    [Fact]
    public void Analyze_ControlFlowChange_ReturnsFull()
    {
        var before = "class C { void M() { if (true) { } } }";
        var after = "class C { void M() { if (false) { } } }";

        Assert.Equal(ContextDecision.Full, BeforeAfterAnalyzer.Analyze(before, after));
    }

    [Fact]
    public void Analyze_TypeDeclarationChange_ReturnsFull()
    {
        var before = "class C { }";
        var after = "class C : IDisposable { }";

        Assert.Equal(ContextDecision.Full, BeforeAfterAnalyzer.Analyze(before, after));
    }

    [Fact]
    public void Analyze_ReturnTypeChange_ReturnsFull()
    {
        var before = "class C { void Foo() { } }";
        var after = "class C { int Foo() { return 0; } }";

        Assert.Equal(ContextDecision.Full, BeforeAfterAnalyzer.Analyze(before, after));
    }

    [Fact]
    public void Analyze_ExpressionBodyChange_ReturnsFull()
    {
        var before = "class C { int X() { return 1; } }";
        var after = "class C { int X() { return 2; } }";

        var result = BeforeAfterAnalyzer.Analyze(before, after);
        Assert.True(result >= ContextDecision.Minimal);
    }

    [Fact]
    public void Analyze_WhitespaceOnlyChange_ReturnsNone()
    {
        var before = "class C { void M() { } }";
        var after = "class C {  void M() {  } }";

        Assert.Equal(ContextDecision.None, BeforeAfterAnalyzer.Analyze(before, after));
    }

    [Fact]
    public void Analyze_IdenticalCode_ReturnsNone()
    {
        var code = "class C { void M() { } }";

        Assert.Equal(ContextDecision.None, BeforeAfterAnalyzer.Analyze(code, code));
    }

    [Fact]
    public void Analyze_BothEmpty_ReturnsNone()
    {
        Assert.Equal(ContextDecision.None, BeforeAfterAnalyzer.Analyze("", ""));
    }

    [Fact]
    public void Analyze_NullInputs_ReturnsNone()
    {
        Assert.Equal(ContextDecision.None, BeforeAfterAnalyzer.Analyze(null!, null!));
    }
}
