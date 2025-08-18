namespace TheDiscDb;

public struct SlugOrIndex : IEquatable<SlugOrIndex>
{
    public const string DefaultValue = "0";

    public string? Slug { get; set; }
    public int? Index { get; set; }

    public string UrlValue
    {
        get
        {
            // Prefer slug for URLs
            if (!string.IsNullOrWhiteSpace(this.Slug))
            {
                return this.Slug;
            }

            if (this.Index.HasValue)
            {
                return this.Index.Value.ToString();
            }
            
            return DefaultValue;
        }
    }

    private SlugOrIndex(int index)
    {
        this.Index = index;
    }

    private SlugOrIndex(string slug)
    {
        this.Slug = slug;
    }

    private SlugOrIndex(string? slug, int? index)
    {
        this.Slug = slug;
        this.Index = index;
    }

    public static SlugOrIndex Create(string? slugOrIndex)
    {
        if (string.IsNullOrWhiteSpace(slugOrIndex))
        {
            return new SlugOrIndex(0);
        }

        if (int.TryParse(slugOrIndex, out var index))
        {
            return new SlugOrIndex(index);
        }

        return new SlugOrIndex(slugOrIndex);
    }

    public static SlugOrIndex Create(string? slug, int? index)
    {
        if (slug == null && index == null)
        {
            throw new ArgumentNullException("slug and index cannot both be null");
        }

        return new SlugOrIndex(slug, index);
    }

    public static implicit operator SlugOrIndex(string input)
    {
        if (int.TryParse(input, out var index))
        {
            return new SlugOrIndex(index);
        }

        return new SlugOrIndex(input);
    }

    public override string? ToString()
    {
        if (this.Index.HasValue)
        {
            return this.Index.ToString();
        }

        return this.Slug;
    }

    public static bool operator ==(SlugOrIndex? left, SlugOrIndex? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    public static bool operator !=(SlugOrIndex? left, SlugOrIndex? right)
    {
        return !(left == right);
    }

    public override bool Equals(object? obj)
    {
        if (obj is SlugOrIndex other)
        {
            return this.Equals(other);
        }

        return base.Equals(obj);
    }

    public override int GetHashCode()
    {
        if (this.Slug != null)
        {
            return this.Slug.GetHashCode();
        }

        return this.Index.GetHashCode();
    }

    public bool Equals(SlugOrIndex other)
    {
        if (Object.Equals(other, null))
        {
            return false;
        }

        if (this.Slug != null && other.Slug != null)
        {
            return this.Slug.Equals(other.Slug, StringComparison.OrdinalIgnoreCase);
        }

        if (this.Index.HasValue && other.Index.HasValue)
        {
            return this.Index.Value == other.Index.Value;
        }

        return false;
    }
}