using System.Text.Json;

namespace Quarp.CartKit;

/// <summary>
/// Parsed manifest.json — <c>{"name","author","profile":8}</c> (Format spec v1, M1 work
/// order). "name" and "profile" are required; "author" defaults to empty. Unknown
/// properties are ignored.
/// </summary>
public sealed class CartManifest
{
    public const int SupportedProfile = 8;

    public required string Name { get; init; }
    public required string Author { get; init; }
    public required int Profile { get; init; }

    public static CartManifest Parse(byte[] utf8Json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json);
        }
        catch (JsonException e)
        {
            throw new CartLoadException($"manifest.json: invalid JSON: {e.Message}", e);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new CartLoadException("manifest.json: top-level value must be a JSON object.");
            }

            if (!root.TryGetProperty("name", out JsonElement nameElement)
                || nameElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(nameElement.GetString()))
            {
                throw new CartLoadException("manifest.json: required string property \"name\" is missing or empty.");
            }

            string author = "";
            if (root.TryGetProperty("author", out JsonElement authorElement))
            {
                if (authorElement.ValueKind != JsonValueKind.String)
                {
                    throw new CartLoadException("manifest.json: property \"author\" must be a string.");
                }
                author = authorElement.GetString()!;
            }

            if (!root.TryGetProperty("profile", out JsonElement profileElement)
                || profileElement.ValueKind != JsonValueKind.Number
                || !profileElement.TryGetInt32(out int profile))
            {
                throw new CartLoadException("manifest.json: required integer property \"profile\" is missing.");
            }
            if (profile != SupportedProfile)
            {
                throw new CartLoadException(
                    $"manifest.json: profile {profile} is not supported; this console runs profile {SupportedProfile}.");
            }

            return new CartManifest
            {
                Name = nameElement.GetString()!,
                Author = author,
                Profile = profile,
            };
        }
    }
}
