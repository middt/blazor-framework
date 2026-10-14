namespace Middt.Framework.Common.Database.Attributes;

/// <summary>Treats zero as a real filter value for numeric properties (by default zero means "no filter").</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class QueryIsZeroAttribute : Attribute;
