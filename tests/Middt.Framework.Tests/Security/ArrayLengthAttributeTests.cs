using System.ComponentModel.DataAnnotations;
using Middt.Framework.Common.Validation;

namespace Middt.Framework.Tests.Security;

public sealed class ArrayLengthAttributeTests
{
    private sealed class Model
    {
        [ArrayMinLength(2)]
        [ArrayMaxLength(3)]
        public List<int>? Values { get; set; }
    }

    private static bool IsValid(List<int>? values)
    {
        var model = new Model { Values = values };
        return Validator.TryValidateObject(model, new ValidationContext(model), null, validateAllProperties: true);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void Collection_size_is_bounded(int count, bool expected)
    {
        IsValid([.. Enumerable.Range(0, count)]).Should().Be(expected);
    }

    [Fact]
    public void Null_is_valid_for_both_attributes()
    {
        IsValid(null).Should().BeTrue();
    }

    [Fact]
    public void Arrays_are_supported_and_non_collections_are_invalid()
    {
        new ArrayMinLengthAttribute(1).IsValid(new[] { "a" }).Should().BeTrue();
        new ArrayMaxLengthAttribute(1).IsValid(new[] { "a", "b" }).Should().BeFalse();
        new ArrayMinLengthAttribute(1).IsValid(42).Should().BeFalse();
        new ArrayMaxLengthAttribute(1).IsValid("text").Should().BeFalse();
    }
}
