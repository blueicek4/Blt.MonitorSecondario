using System.Linq;
using Xunit;

namespace Bluetech.Display.Recorder.Tests;

public class ReceiptDiffTests
{
    static readonly string[] Bar =
    {
        " SPREMUTA ARANCIA         4,00",
        " CAPPUCCINO               1,80",
        " CAFFE ORZO PICCOLO       1,50",
    };

    static System.Collections.Generic.List<DiffLine> Diff(string[] a, string[] b) => ReceiptDiff.Compute(a, b, FileAnalyzer.LineKey);

    [Fact]
    public void Same_item_added_again_appears_at_the_end()
    {
        var current = Bar.Concat(new[] { " CAPPUCCINO               1,80" }).ToArray();
        var added = Diff(Bar, current).Single(d => d.Op == DiffOp.Added);
        Assert.Equal(3, added.Index);
    }

    [Fact]
    public void Repeated_item_appended_after_identical_one_is_still_last()
    {
        var previous = new[] { "A 1,00", "B 2,00", "A 1,00" };
        var current = new[] { "A 1,00", "B 2,00", "A 1,00", "A 1,00" };
        var diff = Diff(previous, current);
        Assert.Equal(new[] { DiffOp.Same, DiffOp.Same, DiffOp.Same, DiffOp.Added }, diff.Select(d => d.Op));
    }

    [Fact]
    public void Voided_line_in_the_middle_is_removed()
    {
        var current = new[] { Bar[0], Bar[2] };
        var diff = Diff(Bar, current);
        var removed = diff.Single(d => d.Op == DiffOp.Removed);
        Assert.Equal(1, removed.Index);
        Assert.Equal(2, diff.Count(d => d.Op == DiffOp.Same));
    }

    [Fact]
    public void Changed_price_is_a_removal_plus_an_addition()
    {
        var current = new[] { Bar[0], " CAPPUCCINO               2,00", Bar[2] };
        var diff = Diff(Bar, current);
        Assert.Single(diff, d => d.Op == DiffOp.Removed && d.Text == Bar[1]);
        Assert.Single(diff, d => d.Op == DiffOp.Added && d.Index == 1);
    }

    [Fact]
    public void Different_padding_is_not_a_change()
    {
        var current = Bar.Select(l => l.Trim().Replace("   ", " ")).ToArray();
        Assert.All(Diff(Bar, current), d => Assert.Equal(DiffOp.Same, d.Op));
    }

    [Fact]
    public void Completely_different_bill_shares_no_lines()
    {
        var diff = Diff(Bar, new[] { " LATTE MACCHIATO          1,60" });
        Assert.DoesNotContain(diff, d => d.Op == DiffOp.Same);
        Assert.Equal(3, diff.Count(d => d.Op == DiffOp.Removed));
    }

    [Fact]
    public void Empty_previous_bill_means_everything_is_added()
    {
        Assert.All(Diff(new string[0], Bar), d => Assert.Equal(DiffOp.Added, d.Op));
    }

    [Fact]
    public void Huge_bills_fall_back_to_full_redraw()
    {
        var big = Enumerable.Range(0, 2100).Select(i => "RIGA " + i).ToArray();
        var diff = Diff(big, big);
        Assert.Equal(2100, diff.Count(d => d.Op == DiffOp.Removed));
        Assert.Equal(2100, diff.Count(d => d.Op == DiffOp.Added));
    }
}
