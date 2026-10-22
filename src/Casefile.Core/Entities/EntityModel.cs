namespace Casefile.Core.Entities;

public enum EntityType { Person, Organisation, Location, Phone, Email, Iban }

/// <summary>An entity mention aggregated per case. <see cref="Key"/> is the normalized identity used for linking.</summary>
public sealed record ExtractedEntity(EntityType Type, string Key, string Display, int Mentions = 1)
{
    public string NodeId => $"{Type}:{Key}";
}

/// <summary>A single mention found in a piece of text, with character offsets.</summary>
public sealed record EntityMention(EntityType Type, string Key, string Display, int Start, int End, string Source);
