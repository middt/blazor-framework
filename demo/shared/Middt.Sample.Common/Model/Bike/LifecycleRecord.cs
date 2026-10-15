#nullable enable
namespace Middt.Sample.Api.Model.Database;

/// <summary>
/// A master data record whose whole life (create, change, close) runs through its lifecycle workflow: the CRUD write
/// endpoints are closed and closing a record only deactivates it (<c>active = false</c>), so orders keep their references.
/// </summary>
public interface ILifecycleRecord
{
    /// <summary>Text of the active flag: <see cref="ActiveText"/> / <see cref="PassiveText"/>.</summary>
    public const string ActiveText = "Aktif";

    /// <inheritdoc cref="ActiveText"/>
    public const string PassiveText = "Pasif";

    /// <summary>
    /// List filter only (not stored): <see cref="ActiveText"/> or <see cref="PassiveText"/> limits <c>getitems</c> to active or
    /// passive records; anything else lists all.
    /// </summary>
    string? Status { get; set; }
}
