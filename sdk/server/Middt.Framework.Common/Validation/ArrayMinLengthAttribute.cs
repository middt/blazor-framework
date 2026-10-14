using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace Middt.Framework.Common.Validation;

/// <summary>Collection must contain at least <c>length</c> items. Null is valid (combine with [Required]).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class ArrayMinLengthAttribute(int length) : ValidationAttribute
{
    public int Length { get; } = length;

    public override bool IsValid(object? value) => value is null || (value is ICollection collection && collection.Count >= Length);
}
