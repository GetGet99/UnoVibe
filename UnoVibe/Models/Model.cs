namespace UnoVibe.Models;

public record Model(string ProviderId, string Id)
{
    public static bool operator ==(Model? m, ModelOption? o)
    {
        if (m is null) return o is null;
        if (o is null) return false;
        if (m.Id == o.Id && m.Id == o.ProviderId) return true;
        return false;
    }
    public static bool operator !=(Model? m, ModelOption? o) => !(m == o);
    public static bool operator ==(ModelOption? o, Model? m) => m == o;
    public static bool operator !=(ModelOption? o, Model? m) => m != o;

    public static Model From(ModelOption o) => new(o.ProviderId, o.Id);
    public string Formatted => $"{ProviderId}/{Id}";
}
