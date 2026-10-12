using EssSimulator.LocalControl;

namespace EssSimulator.Tests.LocalControl;

public class LcAddressExprTests
{
    [Theory]
    [InlineData("17000 + 300 * (n - 1) + 200", 1, 17200)]
    [InlineData("17000 + 300 * (n - 1) + 200", 2, 17500)]
    [InlineData("17000 + 300 * (n - 1) + 200", 3, 17800)]
    [InlineData("27000 + 300 * (n - 1) + 200", 1, 27200)]
    [InlineData("27000 + 300 * (n - 1) + 200", 2, 27500)]
    public void Evaluate_GroupAddressFormula_UsesNAsIndex(string expr, int n, int expected)
    {
        Assert.Equal(expected, LcAddressExpr.Evaluate(expr, n));
        Assert.True(LcAddressExpr.IsGroupScoped(expr));
    }

    [Theory]
    [InlineData("17200", 17200)]
    [InlineData("107", 107)]
    [InlineData(" 33200 ", 33200)]
    public void Evaluate_Literal_IsUnitScoped(string expr, int expected)
    {
        Assert.Equal(expected, LcAddressExpr.Evaluate(expr, 1));
        Assert.Equal(expected, LcAddressExpr.Evaluate(expr, 9));
        Assert.False(LcAddressExpr.IsGroupScoped(expr));
    }

    [Fact]
    public void Compile_CachesDependsOnN()
    {
        var compiled = LcAddressExpr.Compile("17000 + 300 * (n - 1) + 200");
        Assert.True(compiled.DependsOnN);
        Assert.Equal(17200, compiled.Evaluate(1));
        Assert.Equal(17500, compiled.Evaluate(2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("n +")]
    [InlineData("x + 1")]
    [InlineData("1 / 0")]
    public void Evaluate_Invalid_Throws(string expr)
    {
        Assert.ThrowsAny<Exception>(() => LcAddressExpr.Evaluate(expr, 1));
    }

    [Theory]
    [InlineData("param{4 + 28*(n-1)}", 1, "param4")]
    [InlineData("param{4 + 28*(n-1)}", 2, "param32")]
    [InlineData("param{60 + 20*(n-1)}", 1, "param60")]
    [InlineData("param{60 + 20*(n-1)}", 2, "param80")]
    [InlineData("param1", 1, "param1")]
    [InlineData("param1", 2, "param1")]
    public void EvaluateParamName_SubstitutesBraceExpressions(string template, int n, string expected)
    {
        Assert.Equal(expected, LcParamNameExpr.Evaluate(template, n));
    }

    [Fact]
    public void ParamName_Literal_IsNotGroupScoped()
    {
        Assert.False(LcParamNameExpr.IsGroupScoped("param1"));
        Assert.True(LcParamNameExpr.IsGroupScoped("param{4 + 28*(n-1)}"));
    }
}
