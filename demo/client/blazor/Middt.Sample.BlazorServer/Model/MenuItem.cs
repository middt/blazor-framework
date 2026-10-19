namespace Middt.Sample.BlazorServer.Model;

public sealed class MenuItem
{
    public required string Name { get; init; }
    public string? Icon { get; init; }
    public required string Path { get; init; }
    public string? Title { get; init; }
    public bool Expanded { get; set; }
    public IReadOnlyList<MenuItem>? Children { get; set; }
}
