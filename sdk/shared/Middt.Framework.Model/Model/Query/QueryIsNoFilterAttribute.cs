namespace Middt.Framework.Common.Database.Attributes;

/// <summary>Excludes the property from automatic search filtering.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class QueryIsNoFilterAttribute : Attribute;
