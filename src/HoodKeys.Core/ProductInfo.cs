namespace HoodKeys.Core;

/// <summary>Identity of the product, shared by every front end.</summary>
public static class ProductInfo
{
    public const string Name = "HoodKeys";

    public static string Version { get; } = typeof(ProductInfo).Assembly
        .GetName().Version?.ToString(3) ?? "0.0.0";
}
